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

	[TestCase("../../../../etc/passwd")]
	[TestCase("sub/AppIcon")]
	[TestCase("-icon")]
	public async Task An_icon_file_name_that_leaves_the_bundle_is_never_converted(string iconFile)
	{
		var app = Bundle("Evil.app");
		var runner = Plists(new() { [app] = new() { ["CFBundleIconFile"] = iconFile } });
		var catalog = new MacAppCatalog(runner, TimeProvider.System, [_root]);

		var icon = await catalog.GetIconPngAsync(new InstalledApp("com.example.evil", "Evil", app), default);

		Assert.That(icon, Is.Null);
		Assert.That(runner.Calls.Select(call => call.Executable), Does.Not.Contain("/usr/bin/sips"));
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

		monitor.Watch(Slack);
		monitor.Watch(Slack);
		monitor.Unwatch(Slack);
		await monitor.PollAsync();
		var readsWhileWatched = reader.Reads;
		monitor.Unwatch(Slack);
		clock.Advance(TimeSpan.FromMinutes(1));
		await monitor.PollAsync();

		Assert.That(readsWhileWatched, Is.GreaterThan(0));
		Assert.That(reader.Reads, Is.EqualTo(readsWhileWatched));
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
}
