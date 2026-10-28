using System.Text.Json;
using AppBadges.Badges;
using AppBadges.Platform;
using AppBadges.Widget;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using Serilog;

namespace AppBadges;

/// <summary>Offers the App badge widget type and draws its tiles.</summary>
public sealed class PluginIntegration : IPluginIntegration, IWidgetTypeProvider, IUiProvider
{
	public const string WidgetTypeLocalId = "app-badge";

	// The bundle id pattern is repeated in BundleIds; the schema stops a bad value at save time already.
	private const string DataSchema = """
		{
		  "type": "object",
		  "properties": {
		    "bundleId": { "type": "string", "pattern": "^([A-Za-z0-9][A-Za-z0-9.-]{0,254})?$" },
		    "iconSize": { "type": "number" },
		    "badgePosition": { "type": "string", "enum": ["top-right", "top-left", "bottom-right", "bottom-left"] },
		    "badgeStyle": { "type": "string", "enum": ["pill", "square", "outline", "dot"] },
		    "badgeSize": { "type": "number" },
		    "badgeTextColor": { "type": "string" },
		    "border": { "type": "object" },
		    "backgroundColor": { "type": "string" },
		    "label": { "type": "string" },
		    "labelColor": { "type": "string" },
		    "fontFaceId": { "type": "string" },
		    "fontSize": { "type": "number" },
		    "textAlign": { "type": "string" },
		    "labelPosition": { "type": "string" },
		    "accentColor": { "type": "string" }
		  }
		}
		""";

	private readonly ILogger _logger;
	private readonly IAppCatalog _catalog;
	private readonly IAppLauncher _launcher;
	private readonly BadgeMonitor _monitor;
	private readonly IconResources _icons;

	private volatile string? _widgetType;

	public PluginIntegration(ILogger logger, IAppCatalog catalog, IAppLauncher launcher, BadgeMonitor monitor,
		IconResources icons)
	{
		_logger = logger.ForContext<PluginIntegration>();
		_catalog = catalog;
		_launcher = launcher;
		_monitor = monitor;
		_icons = icons;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	/// <summary>Runs on every (re)connection: Macro Deck has forgotten registered icons by then.</summary>
	public Task InitializeAsync(IIntegrationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		_icons.Reset(context.UiResources);
		_ = WarmUpCatalogAsync();
		return Task.CompletedTask;
	}

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		var registration = await context.RegisterWidgetTypeAsync(
			new WidgetTypeDescriptor(
				WidgetTypeLocalId,
				Strings.Widget.Name(),
				Strings.Widget.Description(),
				"{}",
				DataSchema,
				HasConfiguration: true)
			{
				AppearanceProperties =
				[
					WidgetAppearanceProperty.BackgroundColor, WidgetAppearanceProperty.Label,
					WidgetAppearanceProperty.LabelColor, WidgetAppearanceProperty.Font,
					WidgetAppearanceProperty.AccentColor
				]
			},
			cancellationToken).ConfigureAwait(false);

		_widgetType = registration.WidgetTypeId;
	}

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		if (_widgetType is not { } widgetType)
		{
			return null;
		}

		var data = UiWidgetSurface.Data(surface);

		if (UiWidgetSurface.IsConfigurationFor(surface, widgetType))
		{
			var apps = await _catalog.GetAppsAsync(cancellationToken).ConfigureAwait(false);
			return new ViewSession(new UiView(surface, BadgeConfigView.Build(data, apps)));
		}

		if (!UiWidgetSurface.IsFor(surface, widgetType))
		{
			return null;
		}

		var appearance = UiWidgetAppearance.Read(data);
		var caption = string.IsNullOrWhiteSpace(appearance.Label) ? null : appearance.Label.Trim();

		if (UiWidgetSurface.IsSampleRequested(surface))
		{
			return Static(surface, data,
				new BadgeFace(TileState.Running, Strings.Face.SampleName().ToString(), null, "3", null));
		}

		var bundleId = BadgeConfigView.ReadBundleId(data);

		if (!BundleIds.IsValid(bundleId))
		{
			return Static(surface, data, BadgeFace.Unconfigured(caption));
		}

		var app = await FindAppAsync(bundleId!, cancellationToken).ConfigureAwait(false);
		var icon = app is null ? null : await _icons.GetAsync(app).ConfigureAwait(false);

		if (surface.Kind == UiSurfaceKinds.Widget && UiWidgetSurface.WidgetId(surface) is not null)
		{
			return new BadgeWidgetSession(surface, data, app, bundleId!, icon, caption, _monitor, _launcher,
				_logger);
		}

		// An editor draft: the configured app with whatever badge was last read, never a live subscription.
		return Static(surface, data, BadgeFace.For(app, bundleId!, icon, _monitor.Current(bundleId!), caption));
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	private static ViewSession Static(UiSurface surface, JsonElement data, BadgeFace face)
	{
		return new ViewSession(new UiView(surface, BadgeWidgetView.Build(new UiState<BadgeFace>(face), data,
			UiWidgetSafeArea.RadiusOf(surface))));
	}

	private async Task<InstalledApp?> FindAppAsync(string bundleId, CancellationToken cancellationToken)
	{
		var apps = await _catalog.GetAppsAsync(cancellationToken).ConfigureAwait(false);
		return apps.FirstOrDefault(app => string.Equals(app.BundleId, bundleId, StringComparison.OrdinalIgnoreCase));
	}

	private async Task WarmUpCatalogAsync()
	{
		try
		{
			await _catalog.GetAppsAsync(CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Could not list the installed apps");
		}
	}
}
