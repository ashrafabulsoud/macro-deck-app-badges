using AppBadges.Badges;

namespace AppBadges.Platform;

public sealed class MacAppLauncher : IAppLauncher
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly ICommandRunner _runner;

	public MacAppLauncher(ICommandRunner runner)
	{
		_runner = runner;
	}

	/// <remarks>Opens by the bundle path the catalog found, never by a bundle id taken from widget data.</remarks>
	public async Task<bool> OpenAsync(InstalledApp app, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(app);

		if (!Path.IsPathRooted(app.Path) || !Directory.Exists(app.Path))
		{
			return false;
		}

		var result = await _runner.RunAsync("/usr/bin/open", ["-a", app.Path], _timeout, cancellationToken)
			.ConfigureAwait(false);

		return result.ExitCode == 0;
	}
}
