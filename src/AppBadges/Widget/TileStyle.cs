using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;

namespace AppBadges.Widget;

/// <summary>The standard appearance settings, resolved to what the view draws. Mirrors the Action Button.</summary>
internal sealed record TileStyle(
	string? LabelColor,
	double CaptionSize,
	string? FontFace,
	string TextAlign,
	string LabelPosition,
	string BadgeColor,
	double IconSize,
	string? Foreground = null)
{
	// The Dock's own badge red, used until the user picks an accent colour.
	public const string DefaultBadgeColor = "#ff3b30";

	// Smaller than the Action Button's 14 % because the icon takes most of the tile.
	public const double DefaultCaptionSize = 0.075;

	public const string Top = "top";
	public const string Center = "center";
	public const string Bottom = "bottom";

	public static TileStyle From(UiWidgetAppearanceValues appearance, double iconSizePercent = BadgeConfigView.DefaultIconSize)
	{
		ArgumentNullException.ThrowIfNull(appearance);

		// fontSize is a whole-number percentage of the tile; an out-of-range value from an action is clamped.
		var size = appearance.FontSize is { } percent && double.IsFinite(percent)
			? Math.Clamp(percent / 100.0, 0.04, 0.4)
			: DefaultCaptionSize;

		return new TileStyle(
			Blank(appearance.LabelColor),
			size,
			Blank(appearance.FontFaceId),
			appearance.TextAlign is "left" or "right" ? appearance.TextAlign : "center",
			appearance.LabelPosition is Top or Center ? appearance.LabelPosition : Bottom,
			Blank(appearance.AccentColor) ?? DefaultBadgeColor,
			Math.Clamp(iconSizePercent, BadgeConfigView.MinIconSize, BadgeConfigView.MaxIconSize) / 100.0,
			ForegroundOn(appearance.BackgroundColor));
	}

	/// <summary>
	/// White or near-black text for a custom background, or null to follow the theme. Theme text on a
	/// user-picked colour can vanish, such as dark text on a dark purple tile in the light theme.
	/// </summary>
	internal static string? ForegroundOn(string? background)
	{
		if (background is not { Length: 7 or 9 } || background[0] != '#'
			|| !int.TryParse(background.AsSpan(1, 6), System.Globalization.NumberStyles.HexNumber,
				System.Globalization.CultureInfo.InvariantCulture, out var rgb))
		{
			return null;
		}

		// A mostly see-through background shows the theme's tile, so the theme's text still fits.
		if (background.Length == 9
			&& int.TryParse(background.AsSpan(7, 2), System.Globalization.NumberStyles.HexNumber,
				System.Globalization.CultureInfo.InvariantCulture, out var alpha)
			&& alpha < 0x80)
		{
			return null;
		}

		var luminance = (0.299 * (rgb >> 16 & 0xff)) + (0.587 * (rgb >> 8 & 0xff)) + (0.114 * (rgb & 0xff));

		return luminance < 150 ? "#ffffff" : "#1c1c1e";
	}

	public string Justify => TextAlign switch
	{
		"left" => UiComponentJustify.Start,
		"right" => UiComponentJustify.End,
		_ => UiComponentJustify.Center
	};

	public string Align => TextAlign switch
	{
		"left" => UiComponentAlignments.Start,
		"right" => UiComponentAlignments.End,
		_ => UiComponentAlignments.Center
	};

	private static string? Blank(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}
