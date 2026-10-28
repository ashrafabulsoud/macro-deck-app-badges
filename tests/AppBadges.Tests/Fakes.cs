using AppBadges.Badges;
using AppBadges.Platform;

namespace AppBadges.Tests;

internal sealed class FakeCommandRunner : ICommandRunner
{
	private readonly Func<string, IReadOnlyList<string>, CommandResult> _respond;

	public FakeCommandRunner(Func<string, IReadOnlyList<string>, CommandResult> respond)
	{
		_respond = respond;
	}

	public List<(string Executable, IReadOnlyList<string> Arguments)> Calls { get; } = [];

	public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		lock (Calls)
		{
			Calls.Add((executable, arguments));
		}

		return Task.FromResult(_respond(executable, arguments));
	}

	public static CommandResult Ok(string output) => new(0, output, System.Text.Encoding.UTF8.GetBytes(output));

	public static CommandResult Failed() => new(1, string.Empty, []);
}

internal sealed class FakeBadgeReader : IBadgeReader
{
	private readonly Dictionary<string, AppBadge> _badges = new(StringComparer.Ordinal);

	public int Reads { get; private set; }

	public TaskCompletionSource? Gate { get; set; }

	public void Set(string bundleId, AppBadge badge)
	{
		lock (_badges)
		{
			_badges[bundleId] = badge;
		}
	}

	public async Task<AppBadge> ReadAsync(string bundleId, CancellationToken cancellationToken)
	{
		Reads++;

		if (Gate is { } gate)
		{
			await gate.Task.ConfigureAwait(false);
		}

		lock (_badges)
		{
			return _badges.TryGetValue(bundleId, out var badge) ? badge : AppBadge.NotRunning;
		}
	}
}

internal sealed class FakeCatalog : IAppCatalog
{
	public List<InstalledApp> Apps { get; } = [];

	public Task<IReadOnlyList<InstalledApp>> GetAppsAsync(CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<InstalledApp>>(Apps);

	public Task<byte[]?> GetIconPngAsync(InstalledApp app, CancellationToken cancellationToken)
		=> Task.FromResult<byte[]?>(null);
}

internal sealed class FakeLauncher : IAppLauncher
{
	public List<InstalledApp> Opened { get; } = [];

	public bool Succeeds { get; set; } = true;

	public Task<bool> OpenAsync(InstalledApp app, CancellationToken cancellationToken)
	{
		Opened.Add(app);
		return Task.FromResult(Succeeds);
	}
}
