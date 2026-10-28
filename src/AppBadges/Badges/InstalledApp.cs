namespace AppBadges.Badges;

public sealed record InstalledApp(string BundleId, string Name, string Path);

/// <summary>The apps a widget can be pointed at, and their icons. One implementation per operating system.</summary>
public interface IAppCatalog
{
	Task<IReadOnlyList<InstalledApp>> GetAppsAsync(CancellationToken cancellationToken);

	/// <summary>The app's icon as a square PNG, or null when it has none the plugin can read.</summary>
	Task<byte[]?> GetIconPngAsync(InstalledApp app, CancellationToken cancellationToken);
}

public interface IAppLauncher
{
	/// <summary>Opens the app or brings it to the front. False when the system could not.</summary>
	Task<bool> OpenAsync(InstalledApp app, CancellationToken cancellationToken);
}
