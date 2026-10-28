using System.Security.Cryptography;
using System.Text;
using AppBadges.Badges;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using Serilog;

namespace AppBadges.Widget;

/// <summary>Registers each app's icon with Macro Deck once, however many tiles show it.</summary>
public sealed class IconResources
{
	private readonly IAppCatalog _catalog;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();

	private Dictionary<string, Task<UiResource?>> _registered = new(StringComparer.Ordinal);
	private IUiResourceRegistry? _registry;

	public IconResources(IAppCatalog catalog, ILogger logger)
	{
		_catalog = catalog;
		_logger = logger.ForContext<IconResources>();
	}

	/// <summary>Uses the registry of a new initialization, starting from an empty cache.</summary>
	public void Reset(IUiResourceRegistry registry)
	{
		lock (_sync)
		{
			_registry = registry;
			_registered = new Dictionary<string, Task<UiResource?>>(StringComparer.Ordinal);
		}
	}

	public Task<UiResource?> GetAsync(InstalledApp app)
	{
		ArgumentNullException.ThrowIfNull(app);

		lock (_sync)
		{
			if (_registry is not { } registry)
			{
				return Task.FromResult<UiResource?>(null);
			}

			if (!_registered.TryGetValue(app.BundleId, out var registration))
			{
				registration = RegisterAsync(registry, app);
				_registered[app.BundleId] = registration;
			}

			return registration;
		}
	}

	// A failed registration (rate limited, connection dropped) is forgotten so the next tile tries again.
	private void Forget(string bundleId, IUiResourceRegistry registry)
	{
		lock (_sync)
		{
			if (ReferenceEquals(_registry, registry))
			{
				_registered.Remove(bundleId);
			}
		}
	}

	private async Task<UiResource?> RegisterAsync(IUiResourceRegistry registry, InstalledApp app)
	{
		// Never completes inline, so Forget always runs after GetAsync stored this registration.
		await Task.Yield();

		try
		{
			// An app without an icon file stays cached as null: asking again would not find one either.
			var png = await _catalog.GetIconPngAsync(app, CancellationToken.None).ConfigureAwait(false);

			return png is null
				? null
				: await registry.RegisterAsync(NameOf(app.BundleId), png, "image/png").ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Could not register the icon of {BundleId}", app.BundleId);
			Forget(app.BundleId, registry);
			return null;
		}
	}

	// Resource names allow letters, digits, hyphens and underscores only, so the bundle id is hashed.
	internal static string NameOf(string bundleId)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(bundleId));
		return "icon-" + Convert.ToHexStringLower(hash.AsSpan(0, 8));
	}
}
