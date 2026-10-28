using Serilog;

namespace AppBadges.Badges;

public sealed class BadgeChangedEventArgs : EventArgs
{
	public BadgeChangedEventArgs(string bundleId, AppBadge badge)
	{
		BundleId = bundleId;
		Badge = badge;
	}

	public string BundleId { get; }

	public AppBadge Badge { get; }
}

/// <summary>
/// Polls the badges of the apps that open tiles show, and only those: with no tile on screen, nothing runs.
/// Shared by every session so one app shown on several screens is read once per interval.
/// </summary>
public sealed class BadgeMonitor : IDisposable
{
	internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

	private readonly IBadgeReader _reader;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, Watched> _watched = new(StringComparer.Ordinal);
	private readonly ITimer _timer;

	private int _polling;
	private bool _failing;
	private bool _disposed;

	public BadgeMonitor(IBadgeReader reader, TimeProvider time, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(time);

		_reader = reader;
		_logger = logger.ForContext<BadgeMonitor>();
		_timer = time.CreateTimer(_ => _ = PollAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	/// <summary>Raised when an app's badge or running state changed. May be raised on any thread.</summary>
	public event EventHandler<BadgeChangedEventArgs>? Changed;

	/// <summary>The last badge read for the app, or null before the first read.</summary>
	public AppBadge? Current(string bundleId)
	{
		lock (_sync)
		{
			return _watched.TryGetValue(bundleId, out var watched) ? watched.Badge : null;
		}
	}

	/// <summary>Starts reading the app's badge; every call needs a matching <see cref="Unwatch" />.</summary>
	public void Watch(string bundleId)
	{
		ArgumentException.ThrowIfNullOrEmpty(bundleId);

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			if (_watched.TryGetValue(bundleId, out var watched))
			{
				watched.Count++;
				return;
			}

			_watched[bundleId] = new Watched();

			// The first watcher should not wait a whole interval for its first number.
			_timer.Change(TimeSpan.Zero, PollInterval);
		}
	}

	public void Unwatch(string bundleId)
	{
		lock (_sync)
		{
			if (!_watched.TryGetValue(bundleId, out var watched) || --watched.Count > 0)
			{
				return;
			}

			_watched.Remove(bundleId);

			if (_watched.Count == 0 && !_disposed)
			{
				_timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
			}
		}
	}

	public void Dispose()
	{
		lock (_sync)
		{
			_disposed = true;
		}

		_timer.Dispose();
	}

	// Internal so tests can drive a poll without waiting for the timer.
	internal async Task PollAsync()
	{
		// A slow poll is never overlapped by the next tick.
		if (Interlocked.Exchange(ref _polling, 1) == 1)
		{
			return;
		}

		try
		{
			string[] bundleIds;

			lock (_sync)
			{
				bundleIds = [.. _watched.Keys];
			}

			foreach (var bundleId in bundleIds)
			{
				await ReadOneAsync(bundleId).ConfigureAwait(false);
			}
		}
		finally
		{
			Volatile.Write(ref _polling, 0);
		}
	}

	private async Task ReadOneAsync(string bundleId)
	{
		AppBadge badge;

		try
		{
			badge = await _reader.ReadAsync(bundleId, CancellationToken.None).ConfigureAwait(false);

			if (_failing)
			{
				_failing = false;
				_logger.Information("Reading app badges works again");
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// Logged once per failure episode, not on every two-second poll.
			if (!_failing)
			{
				_failing = true;
				_logger.Warning(exception, "Could not read the badge of {BundleId}", bundleId);
			}

			return;
		}

		lock (_sync)
		{
			if (!_watched.TryGetValue(bundleId, out var watched) || watched.Badge == badge)
			{
				return;
			}

			watched.Badge = badge;
		}

		Changed?.Invoke(this, new BadgeChangedEventArgs(bundleId, badge));
	}

	private sealed class Watched
	{
		public int Count { get; set; } = 1;

		public AppBadge? Badge { get; set; }
	}
}
