using MacroDeck.Sdk.Logging;
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
	private readonly TimeProvider _time;

	private int _polling;
	private bool _disposed;

	public BadgeMonitor(IBadgeReader reader, TimeProvider time, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(time);

		_reader = reader;
		_time = time;
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
		FailureEpisodeTracker failures;

		lock (_sync)
		{
			if (!_watched.TryGetValue(bundleId, out var watched))
			{
				return;
			}

			failures = watched.Failures ??= new FailureEpisodeTracker(time: _time);
		}

		try
		{
			badge = await _reader.ReadAsync(bundleId, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// One episode per app, so a single failing app neither floods the log nor hides another's recovery.
			var signal = failures.RecordFailure(exception.Message);

			switch (signal.Kind)
			{
				case FailureEpisodeSignalKind.Onset:
					_logger.Warning(exception, "Could not read the badge of {BundleId}", bundleId);
					break;
				case FailureEpisodeSignalKind.SummaryDue:
					_logger.Warning("Reading the badge of {BundleId} has failed {Failures} times in {Duration}: {Error}",
						bundleId, signal.ConsecutiveFailures, signal.Duration, signal.LastError);
					break;
				default:
					_logger.Debug("Reading the badge of {BundleId} failed again: {Error}", bundleId, signal.LastError);
					break;
			}

			return;
		}

		if (failures.RecordSuccess() is { } recovered)
		{
			_logger.Information("Reading the badge of {BundleId} works again after {Failures} failures",
				bundleId, recovered.Failures);
		}

		lock (_sync)
		{
			if (!_watched.TryGetValue(bundleId, out var watched) || watched.Badge == badge)
			{
				return;
			}

			watched.Badge = badge;
		}

		Raise(new BadgeChangedEventArgs(bundleId, badge));
	}

	// The badge is already stored, so a throwing subscriber must not keep the others from hearing about it.
	private void Raise(BadgeChangedEventArgs args)
	{
		if (Changed is not { } changed)
		{
			return;
		}

		foreach (var handler in changed.GetInvocationList().Cast<EventHandler<BadgeChangedEventArgs>>())
		{
			try
			{
				handler(this, args);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "A tile failed to show the new badge of {BundleId}", args.BundleId);
			}
		}
	}

	private sealed class Watched
	{
		public int Count { get; set; } = 1;

		public AppBadge? Badge { get; set; }

		public FailureEpisodeTracker? Failures { get; set; }
	}
}
