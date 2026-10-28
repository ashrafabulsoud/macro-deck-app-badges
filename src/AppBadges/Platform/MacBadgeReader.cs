using AppBadges.Badges;

namespace AppBadges.Platform;

public sealed class MacBadgeReader : IBadgeReader
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

	private readonly ICommandRunner _runner;

	public MacBadgeReader(ICommandRunner runner)
	{
		_runner = runner;
	}

	public async Task<AppBadge> ReadAsync(string bundleId, CancellationToken cancellationToken)
	{
		if (!BundleIds.IsValid(bundleId))
		{
			return AppBadge.NotRunning;
		}

		var result = await _runner.RunAsync("/usr/bin/lsappinfo", ["info", "-long", "-app", bundleId], _timeout,
			cancellationToken).ConfigureAwait(false);

		return result.ExitCode == 0 ? StatusLabelParser.Parse(result.Output) : AppBadge.NotRunning;
	}
}
