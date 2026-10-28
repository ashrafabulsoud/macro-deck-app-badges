namespace AppBadges.Badges;

/// <summary>What the Dock shows for an app: whether it runs, and its badge text if it has one.</summary>
public sealed record AppBadge(bool IsRunning, string? Label)
{
	public static AppBadge NotRunning { get; } = new(false, null);

	public bool HasBadge => IsRunning && Label is not null;
}

/// <summary>Reads an app's Dock badge. One implementation per operating system.</summary>
public interface IBadgeReader
{
	Task<AppBadge> ReadAsync(string bundleId, CancellationToken cancellationToken);
}
