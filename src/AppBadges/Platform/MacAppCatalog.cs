using System.Collections.Concurrent;
using AppBadges.Badges;

namespace AppBadges.Platform;

public sealed class MacAppCatalog : IAppCatalog
{
	private const int IconSize = 256;

	private static readonly TimeSpan _toolTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _scanFor = TimeSpan.FromMinutes(10);

	private readonly ICommandRunner _runner;
	private readonly TimeProvider _time;
	private readonly IReadOnlyList<string> _roots;
	private readonly Lock _sync = new();
	// Keyed by the Info.plist's modification time too, which an app update rewrites.
	private readonly ConcurrentDictionary<(string BundleId, string Path, DateTime Modified), byte[]?> _icons = new();

	private IReadOnlyList<InstalledApp>? _apps;
	private DateTimeOffset _scannedAt;
	private Task<IReadOnlyList<InstalledApp>>? _scan;

	public MacAppCatalog(ICommandRunner runner, TimeProvider time)
		: this(runner, time,
		[
			"/Applications",
			"/System/Applications",
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications")
		])
	{
	}

	public MacAppCatalog(ICommandRunner runner, TimeProvider time, IReadOnlyList<string> roots)
	{
		_runner = runner;
		_time = time;
		_roots = roots;
	}

	public async Task<IReadOnlyList<InstalledApp>> GetAppsAsync(CancellationToken cancellationToken)
	{
		Task<IReadOnlyList<InstalledApp>> scan;

		lock (_sync)
		{
			if (_apps is { } cached && _time.GetUtcNow() - _scannedAt < _scanFor)
			{
				return cached;
			}

			// One shared scan; a caller that gives up stops waiting without cancelling it for the others.
			scan = _scan ??= ScanAsync();
		}

		return await scan.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<IReadOnlyList<InstalledApp>> ScanAsync()
	{
		try
		{
			var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

			foreach (var bundle in _roots.SelectMany(root => FindBundles(root, depth: 0)))
			{
				InstalledApp? app;

				try
				{
					app = await ReadAppAsync(bundle, CancellationToken.None).ConfigureAwait(false);
				}
				catch (Exception exception) when (exception is not OutOfMemoryException)
				{
					// One unreadable bundle must not hide every other app.
					continue;
				}

				if (app is not null)
				{
					// The first root wins, so /Applications beats a web-app shim of the same app in ~/Applications.
					apps.TryAdd(app.BundleId, app);
				}
			}

			IReadOnlyList<InstalledApp> sorted =
				[.. apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)];

			lock (_sync)
			{
				_apps = sorted;
				_scannedAt = _time.GetUtcNow();
			}

			return sorted;
		}
		finally
		{
			lock (_sync)
			{
				_scan = null;
			}
		}
	}

	public async Task<byte[]?> GetIconPngAsync(InstalledApp app, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(app);

		var key = (app.BundleId, app.Path, File.GetLastWriteTimeUtc(Path.Combine(app.Path, "Contents", "Info.plist")));

		if (_icons.TryGetValue(key, out var cached))
		{
			return cached;
		}

		var icon = await RenderIconAsync(app, cancellationToken).ConfigureAwait(false);
		_icons[key] = icon;
		return icon;
	}

	private async Task<byte[]?> RenderIconAsync(InstalledApp app, CancellationToken cancellationToken)
	{
		var iconFile = await ReadPlistValueAsync(app.Path, "CFBundleIconFile", cancellationToken).ConfigureAwait(false);

		// Info.plist belongs to the app, not to us: accept a bare file name inside Contents/Resources only.
		if (string.IsNullOrWhiteSpace(iconFile) || iconFile != Path.GetFileName(iconFile) || iconFile.StartsWith('-'))
		{
			return null;
		}

		var resources = Path.GetFullPath(Path.Combine(app.Path, "Contents", "Resources")) + Path.DirectorySeparatorChar;
		var icns = Path.GetFullPath(Path.Combine(resources,
			iconFile.EndsWith(".icns", StringComparison.OrdinalIgnoreCase) ? iconFile : iconFile + ".icns"));

		if (!icns.StartsWith(resources, StringComparison.Ordinal) || !File.Exists(icns))
		{
			return null;
		}

		var output = Path.Combine(Path.GetTempPath(), $"app-badges-{Guid.NewGuid():N}.png");

		try
		{
			var result = await _runner.RunAsync("/usr/bin/sips",
				["-s", "format", "png", "-Z", IconSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
					icns, "--out", output], _toolTimeout, cancellationToken).ConfigureAwait(false);

			return result.ExitCode == 0 && File.Exists(output)
				? await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false)
				: null;
		}
		finally
		{
			File.Delete(output);
		}
	}

	private async Task<InstalledApp?> ReadAppAsync(string bundle, CancellationToken cancellationToken)
	{
		var bundleId = await ReadPlistValueAsync(bundle, "CFBundleIdentifier", cancellationToken).ConfigureAwait(false);

		if (!BundleIds.IsValid(bundleId))
		{
			return null;
		}

		return new InstalledApp(bundleId!, Path.GetFileNameWithoutExtension(bundle), bundle);
	}

	private async Task<string?> ReadPlistValueAsync(string bundle, string key, CancellationToken cancellationToken)
	{
		var plist = Path.Combine(bundle, "Contents", "Info.plist");

		if (!File.Exists(plist))
		{
			return null;
		}

		var result = await _runner.RunAsync("/usr/bin/plutil", ["-extract", key, "raw", "-o", "-", plist],
			_toolTimeout, cancellationToken).ConfigureAwait(false);

		return result.ExitCode == 0 ? result.Output.Trim() : null;
	}

	// Never descends into a bundle: helper apps inside one are not apps the user installed.
	private static IEnumerable<string> FindBundles(string directory, int depth)
	{
		IEnumerable<string> entries;

		try
		{
			entries = Directory.EnumerateDirectories(directory).ToList();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			yield break;
		}

		foreach (var entry in entries.Order(StringComparer.Ordinal))
		{
			if (entry.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
			{
				yield return entry;
			}
			else if (depth < 2)
			{
				foreach (var nested in FindBundles(entry, depth + 1))
				{
					yield return nested;
				}
			}
		}
	}
}
