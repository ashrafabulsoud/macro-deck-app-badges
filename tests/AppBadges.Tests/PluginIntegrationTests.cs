using System.Text.Json;
using AppBadges.Badges;
using AppBadges.Widget;
using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Surfaces;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace AppBadges.Tests;

[TestFixture]
public sealed class PluginIntegrationTests
{
	private const string Slack = "com.tinyspeck.slackmacgap";

	private FakeCatalog _catalog = null!;
	private FakeLauncher _launcher = null!;
	private FakeBadgeReader _reader = null!;

	private PluginTestHarness CreateHarness() =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddSingleton<IAppCatalog>(_catalog);
			builder.Services.AddSingleton<IAppLauncher>(_launcher);
			builder.Services.AddSingleton<IBadgeReader>(_reader);
			builder.Services.AddSingleton<BadgeMonitor>();
			builder.Services.AddSingleton<IconResources>();
			builder
				.UseLocalization(Strings.LocalizationCatalog)
				.RegisterIntegration<PluginIntegration>();
		});

	private static PluginIntegration IntegrationOf(PluginTestHarness harness) =>
		harness.Services.GetServices<IPluginIntegration>().OfType<PluginIntegration>().Single();

	private static async Task<PluginIntegration> ReadyAsync(PluginTestHarness harness)
	{
		await harness.InitializeIntegrationsAsync();
		var integration = IntegrationOf(harness);
		await integration.InitializeAsync(new FakeWidgetTypeProviderContext());
		return integration;
	}

	// NUnit reuses one fixture instance, so every test gets fresh fakes.
	[SetUp]
	public void CreateFakes()
	{
		_catalog = new FakeCatalog();
		_launcher = new FakeLauncher();
		_reader = new FakeBadgeReader();
		_catalog.Apps.Add(new InstalledApp(Slack, "Slack", "/Applications/Slack.app"));
	}

	[Test]
	public async Task The_widget_type_is_registered_as_configurable()
	{
		await using var harness = CreateHarness();
		var context = new FakeWidgetTypeProviderContext();

		await IntegrationOf(harness).InitializeAsync(context);

		Assert.That(context.WidgetTypes[PluginIntegration.WidgetTypeLocalId].HasConfiguration, Is.True);
	}

	[Test]
	public async Task Surfaces_of_another_widget_type_are_declined()
	{
		await using var harness = CreateHarness();
		var integration = await ReadyAsync(harness);

		var session = await integration.CreateSessionAsync(Request("someone.else::clock", $$"""{"bundleId":"{{Slack}}"}"""), default);

		Assert.That(session, Is.Null);
	}

	[Test]
	public async Task A_tap_opens_the_configured_app_by_its_catalog_entry()
	{
		await using var harness = CreateHarness();
		var integration = await ReadyAsync(harness);
		await using var session = (await integration.CreateSessionAsync(
			Request(PluginIntegration.WidgetTypeLocalId, $$"""{"bundleId":"{{Slack}}"}"""), default))!;

		session.Dispatch(Press(session), new FakeUiEventOrigin());
		await Task.Delay(50);

		Assert.That(_launcher.Opened.Single().Path, Is.EqualTo("/Applications/Slack.app"));
	}

	[Test]
	public async Task A_failed_open_is_reported_to_the_screen_that_tapped()
	{
		_launcher.Succeeds = false;
		await using var harness = CreateHarness();
		var integration = await ReadyAsync(harness);
		await using var session = (await integration.CreateSessionAsync(
			Request(PluginIntegration.WidgetTypeLocalId, $$"""{"bundleId":"{{Slack}}"}"""), default))!;
		var origin = new FakeUiEventOrigin();

		session.Dispatch(Press(session), origin);
		await Task.Delay(50);

		Assert.That(origin.ReportedFailures, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_bundle_id_that_is_not_installed_never_launches_anything()
	{
		await using var harness = CreateHarness();
		var integration = await ReadyAsync(harness);
		await using var session = (await integration.CreateSessionAsync(
			Request(PluginIntegration.WidgetTypeLocalId, """{"bundleId":"com.example.gone"}"""), default))!;

		session.Dispatch(Press(session), new FakeUiEventOrigin());
		await Task.Delay(50);

		Assert.That(_launcher.Opened, Is.Empty);
	}

	[Test]
	public async Task A_live_tile_follows_the_badge()
	{
		await using var harness = CreateHarness();
		var integration = await ReadyAsync(harness);
		await using var session = (await integration.CreateSessionAsync(
			Request(PluginIntegration.WidgetTypeLocalId, $$"""{"bundleId":"{{Slack}}"}"""), default))!;
		session.DrainPatches();

		_reader.Set(Slack, new AppBadge(true, "4"));
		await harness.Services.GetRequiredService<BadgeMonitor>().PollAsync();

		Assert.That(session.DrainPatches(), Is.Not.Empty);
	}

	private static UiEvent Press(IUiSession session) =>
		new() { NodeId = session.BuildTree().Root.Id, Name = UiComponentEvents.Press };

	private static UiSessionRequest Request(string widgetType, string data) => new()
	{
		UiModelVersion = 1,
		Surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>
			{
				[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse(data).RootElement.Clone()
			}
		}
	};
}

[TestFixture]
public sealed class LocalizationTests
{
	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.ashrafabulsoud.app-badges"));
	}

	[Test]
	public void Every_key_the_default_culture_declares_resolves_to_text()
	{
		foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
		{
			Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text), Is.True);
			Assert.That(text, Is.Not.Empty);
		}
	}
}
