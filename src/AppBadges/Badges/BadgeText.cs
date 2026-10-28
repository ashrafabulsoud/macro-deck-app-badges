namespace AppBadges.Badges;

public static class BadgeText
{
	public const int MaxShown = 99;

	/// <summary>
	/// What the badge shows: numbers up to 99, then "99+"; other text as the app wrote it, shortened; an empty
	/// label (an app asking for attention without a count) is drawn as a dot, so it reads as an empty string here.
	/// </summary>
	public static string Format(string label)
	{
		ArgumentNullException.ThrowIfNull(label);

		var trimmed = label.Trim();

		if (int.TryParse(trimmed, System.Globalization.NumberStyles.None,
				System.Globalization.CultureInfo.InvariantCulture, out var count))
		{
			return count > MaxShown ? $"{MaxShown}+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		return trimmed.Length <= 4 ? trimmed : trimmed[..3] + "…";
	}
}
