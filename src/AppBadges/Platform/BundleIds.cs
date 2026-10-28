using System.Text.RegularExpressions;

namespace AppBadges.Platform;

public static partial class BundleIds
{
	/// <summary>Whether <paramref name="bundleId" /> looks like a macOS bundle identifier and nothing else.</summary>
	public static bool IsValid(string? bundleId)
	{
		return bundleId is not null && Pattern().IsMatch(bundleId);
	}

	[GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.-]{0,254}$")]
	private static partial Regex Pattern();
}
