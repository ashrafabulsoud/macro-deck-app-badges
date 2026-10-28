using AppBadges.Badges;
using AppBadges.Platform;
using AppBadges.Widget;
using MacroDeck.Plugin.Testing;
using NUnit.Framework;
using Serilog;

namespace AppBadges.Tests;

[TestFixture]
public sealed class AppCatalogTests
{
	private string _root = null!;

	[SetUp]
	public void CreateRoot()
	{
		_root = Path.Combine(Path.GetTempPath(), "app-badges-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void DeleteRoot()
	{
		Directory.Delete(_root, recursive: true);
	}

	private string Bundle(string relative)
	{
		var bundle = Path.Combine(_root, relative);
		Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
		File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), "plist");
		return bundle;
	}

	private static FakeCommandRunner Plists(Dictionary<string, Dictionary<string, string>> values) =>
		new((executable, arguments) =>
		{
			if (executable != "/usr/bin/plutil")
			{
				return FakeCommandRunner.Failed();
			}

			var bundle = Path.GetDirectoryName(Path.GetDirectoryName(arguments[^1]))!;
			return values.TryGetValue(bundle, out var keys) && keys.TryGetValue(arguments[1], out var value)
				? FakeCommandRunner.Ok(value + "\n")
				: FakeCommandRunner.Failed();
		});

	[Test]
	public async Task Apps_are_found_with_their_bundle_ids_and_bundles_are_not_searched_inside()
	{
		var slack = Bundle("Slack.app");
		var helper = Bundle("Slack.app/Contents/Frameworks/Helper.app");
		var nested = Bundle("Utilities/Terminal.app");
		var runner = Plists(new()
		{
			[slack] = new() { ["CFBundleIdentifier"] = "com.tinyspeck.slackmacgap" },
			[helper] = new() { ["CFBundleIdentifier"] = "com.tinyspeck.helper" },
			[nested] = new() { ["CFBundleIdentifier"] = "com.apple.Terminal" }
		});

		var apps = await new MacAppCatalog(runner, TimeProvider.System, [_root]).GetAppsAsync(default);

		Assert.That(apps.Select(app => app.BundleId),
			Is.EquivalentTo(new[] { "com.tinyspeck.slackmacgap", "com.apple.Terminal" }));
	}

	[Test]
	public async Task A_bundle_whose_id_is_not_a_plain_bundle_id_is_skipped()
	{
		var bad = Bundle("Bad.app");
		var runner = Plists(new() { [bad] = new() { ["CFBundleIdentifier"] = "-a /System" } });

		var apps = await new MacAppCatalog(runner, TimeProvider.System, [_root]).GetAppsAsync(default);

		Assert.That(apps, Is.Empty);
	}

	// The targets exist, so only the name check can be what keeps them from sips.
	[TestCase("../../../outside")]
	[TestCase("sub/AppIcon")]
	[TestCase("-icon")]
	public async Task An_icon_file_name_that_leaves_the_bundle_is_never_converted(string iconFile)
	{
		var app = Bundle("Evil.app");
		File.WriteAllText(Path.Combine(_root, "outside.icns"), "icns");
		Directory.CreateDirectory(Path.Combine(app, "Contents", "Resources", "sub"));
		File.WriteAllText(Path.Combine(app, "Contents", "Resources", "sub", "AppIcon.icns"), "icns");
		File.WriteAllText(Path.Combine(app, "Contents", "Resources", "-icon.icns"), "icns");
		var runner = Plists(new() { [app] = new() { ["CFBundleIconFile"] = iconFile } });
		var catalog = new MacAppCatalog(runner, TimeProvider.System, [_root]);

		var icon = await catalog.GetIconPngAsync(new InstalledApp("com.example.evil", "Evil", app), default);

		Assert.That(icon, Is.Null);
		Assert.That(runner.Calls.Select(call => call.Executable), Does.Not.Contain("/usr/bin/sips"));
	}

	[Test]
	public async Task An_icon_inside_the_bundle_is_converted()
	{
		var app = Bundle("Good.app");
		File.WriteAllText(Path.Combine(app, "Contents", "Resources", "AppIcon.icns"), "icns");
		var runner = Plists(new() { [app] = new() { ["CFBundleIconFile"] = "AppIcon" } });

		await new MacAppCatalog(runner, TimeProvider.System, [_root])
			.GetIconPngAsync(new InstalledApp("com.example.good", "Good", app), default);

		var sips = runner.Calls.Single(call => call.Executable == "/usr/bin/sips");
		Assert.That(sips.Arguments, Does.Contain(Path.Combine(app, "Contents", "Resources", "AppIcon.icns")));
	}

	[Test]
	public async Task A_bundle_that_cannot_be_read_does_not_hide_the_others()
	{
		var broken = Bundle("Broken.app");
		var slack = Bundle("Slack.app");
		var runner = new FakeCommandRunner((_, arguments) => arguments[^1].StartsWith(broken, StringComparison.Ordinal)
			? throw new OperationCanceledException()
			: FakeCommandRunner.Ok("com.tinyspeck.slackmacgap\n"));

		var apps = await new MacAppCatalog(runner, TimeProvider.System, [_root]).GetAppsAsync(default);

		Assert.That(apps.Select(app => app.Path), Is.EqualTo(new[] { slack }));
	}

	[Test]
	public void Apps_sharing_a_name_are_told_apart_by_bundle_id()
	{
		var options = BadgeConfigView.Options(
		[
			new InstalledApp("com.hnc.Discord", "Discord", "/a"),
			new InstalledApp("com.hnc.DiscordPTB", "Discord", "/b"),
			new InstalledApp("com.tinyspeck.slackmacgap", "Slack", "/c")
		]);

		Assert.That(options.Select(option => option.Label.ToString()), Is.EquivalentTo(new[]
		{
			"Discord (com.hnc.Discord)", "Discord (com.hnc.DiscordPTB)", "Slack"
		}));
	}
}

[TestFixture]
public sealed class BadgeMonitorTests
{
	private const string Slack = "com.tinyspeck.slackmacgap";

	private static BadgeMonitor Monitor(FakeBadgeReader reader, ManualTimeProvider clock) =>
		new(reader, clock, new LoggerConfiguration().CreateLogger());

	[Test]
	public async Task A_new_badge_is_reported_once()
	{
		var reader = new FakeBadgeReader();
		using var monitor = Monitor(reader, new ManualTimeProvider());
		var changes = new List<AppBadge>();
		monitor.Changed += (_, args) => changes.Add(args.Badge);

		reader.Set(Slack, new AppBadge(true, "17"));
		monitor.Watch(Slack);
		await monitor.PollAsync();
		await monitor.PollAsync();

		Assert.That(changes, Is.EqualTo(new[] { new AppBadge(true, "17") }));
		Assert.That(monitor.Current(Slack), Is.EqualTo(new AppBadge(true, "17")));
	}

	[Test]
	public async Task Nothing_is_read_once_no_tile_watches_the_app()
	{
		var reader = new FakeBadgeReader();
		var clock = new ManualTimeProvider();
		using var monitor = Monitor(reader, clock);

		// Only the timer drives reads here, so this also proves the timer stops.
		monitor.Watch(Slack);
		monitor.Watch(Slack);
		monitor.Unwatch(Slack);
		clock.Advance(BadgeMonitor.PollInterval);
		var readsWhileWatched = reader.Reads;
		monitor.Unwatch(Slack);
		clock.Advance(TimeSpan.FromMinutes(1));

		Assert.That(readsWhileWatched, Is.GreaterThanOrEqualTo(2));
		Assert.That(reader.Reads, Is.EqualTo(readsWhileWatched));
	}

	[Test]
	public async Task A_failing_tile_does_not_keep_the_others_from_the_new_badge()
	{
		var reader = new FakeBadgeReader();
		using var monitor = Monitor(reader, new ManualTimeProvider());
		var heard = 0;
		monitor.Changed += (_, _) => throw new InvalidOperationException("tile is broken");
		monitor.Changed += (_, _) => heard++;

		reader.Set(Slack, new AppBadge(true, "3"));
		monitor.Watch(Slack);
		await monitor.PollAsync();

		Assert.That(heard, Is.EqualTo(1));
	}

	[Test]
	public async Task A_slow_poll_is_not_overlapped_by_the_next()
	{
		var reader = new FakeBadgeReader { Gate = new TaskCompletionSource() };
		using var monitor = Monitor(reader, new ManualTimeProvider());
		monitor.Watch(Slack);

		var first = monitor.PollAsync();
		await monitor.PollAsync();
		reader.Gate.SetResult();
		await first;

		Assert.That(reader.Reads, Is.EqualTo(1));
	}
}

[TestFixture]
public sealed class ProcessCommandRunnerTests
{
	[Test]
	[Platform("MacOsX,Linux")]
	public void A_tool_that_runs_too_long_is_stopped()
	{
		var runner = new ProcessCommandRunner();
		var started = DateTime.UtcNow;

		Assert.That(async () => await runner.RunAsync("/bin/sleep", ["30"], TimeSpan.FromMilliseconds(200), default),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(10)));
	}

	[Test]
	[Platform("MacOsX,Linux")]
	public void A_tool_that_prints_too_much_is_stopped_without_waiting_for_the_timeout()
	{
		var runner = new ProcessCommandRunner();
		var started = DateTime.UtcNow;

		Assert.That(async () => await runner.RunAsync("/usr/bin/yes", [], TimeSpan.FromSeconds(60), default),
			Throws.InstanceOf<InvalidDataException>());
		Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(20)));
	}
}
