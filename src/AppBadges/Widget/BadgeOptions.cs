using System.Text.Json;
using MacroDeck.Ui.Components;

namespace AppBadges.Widget;

/// <summary>How the message badge is drawn: where, how big, in which style and with which text colour.</summary>
internal sealed record BadgeOptions(string Position, double Scale, string Style, string TextColor)
{
	public const string PositionKey = "badgePosition";
	public const string SizeKey = "badgeSize";
	public const string StyleKey = "badgeStyle";
	public const string TextColorKey = "badgeTextColor";

	public const string TopRight = "top-right";
	public const string TopLeft = "top-left";
	public const string BottomRight = "bottom-right";
	public const string BottomLeft = "bottom-left";

	public const string Pill = "pill";
	public const string Square = "square";
	public const string Outline = "outline";
	public const string Dot = "dot";

	public const double DefaultSize = 100;
	public const double MinSize = 50;
	public const double MaxSize = 200;

	// White on the badge colour, as in the Dock.
	public const string DefaultTextColor = "#ffffff";

	// An outline badge sits on a dark fill so its coloured ring and number stay readable on any background.
	public const string OutlineFill = "#1c1c1e";

	public static readonly BadgeOptions Default = new(TopRight, 1, Pill, DefaultTextColor);

	public static IReadOnlyList<string> Positions { get; } = [TopRight, TopLeft, BottomRight, BottomLeft];

	public static IReadOnlyList<string> Styles { get; } = [Pill, Square, Outline, Dot];

	public bool IsTop => Position is TopRight or TopLeft;

	public bool IsRight => Position is TopRight or BottomRight;

	public bool ShowsNumber => Style != Dot;

	public static BadgeOptions Read(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return Default;
		}

		var position = ReadString(data, PositionKey);
		var style = ReadString(data, StyleKey);
		var size = data.TryGetProperty(SizeKey, out var sizeValue)
			&& sizeValue.ValueKind == JsonValueKind.Number
			&& sizeValue.TryGetDouble(out var percent)
			&& double.IsFinite(percent)
				? Math.Clamp(percent, MinSize, MaxSize)
				: DefaultSize;

		return new BadgeOptions(
			position is not null && Positions.Contains(position) ? position : TopRight,
			size / 100.0,
			style is not null && Styles.Contains(style) ? style : Pill,
			ReadString(data, TextColorKey) ?? DefaultTextColor);
	}

	/// <summary>The badge's shape for the number of characters it holds (1 is the dot).</summary>
	public string ShapeFor(int width)
	{
		return Style == Square ? UiComponentShapes.RoundedRectangle
			: width == 1 ? UiComponentShapes.Circle
			: UiComponentShapes.Capsule;
	}

	private static string? ReadString(JsonElement data, string key)
	{
		return data.TryGetProperty(key, out var value)
			&& value.ValueKind == JsonValueKind.String
			&& !string.IsNullOrWhiteSpace(value.GetString())
				? value.GetString()!.Trim()
				: null;
	}
}
