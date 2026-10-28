using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;

namespace AppBadges.Widget;

internal static class BadgeWidgetView
{
	// Secondary and muted text on a custom background: the chosen foreground made partly see-through.
	private const string SecondaryAlpha = "cc";
	private const string MutedAlpha = "8c";

	public static UiElement Build(
		UiState<BadgeFace> face,
		JsonElement data,
		int cornerRadius,
		IReadOnlyList<UiEventHandler>? events = null)
	{
		ArgumentNullException.ThrowIfNull(face);

		var appearance = UiWidgetAppearance.Read(data);
		var safeArea = UiWidgetSafeArea.For(cornerRadius);
		var style = TileStyle.From(appearance, BadgeConfigView.ReadIconSize(data));
		var badge = BadgeOptions.Read(data);

		return new UiStack
		{
			Key = "app",
			Fill = true,
			Background = appearance.BackgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Events = events ?? [],
			Children =
			[
				new UiWhen
				{
					Key = "unconfiguredGate",
					Condition = () => !face.Value.IsConfigured,
					Content = () => Unconfigured(safeArea, style)
				},
				new UiWhen
				{
					Key = "configuredGate",
					Condition = () => face.Value.IsConfigured,
					Content = () => new UiLayer
					{
						Key = "tile",
						Fill = true,
						Children = style.LabelPosition == TileStyle.Center
							?
							[
								Body(face, safeArea, style), CenteredCaption(face, safeArea, style),
								BadgeCorner(face, safeArea, style, badge)
							]
							: [Body(face, safeArea, style), BadgeCorner(face, safeArea, style, badge)]
					}
				}
			]
		};
	}

	private static UiStack Body(UiState<BadgeFace> face, UiSize safeArea, TileStyle style)
	{
		List<UiElement> children =
		[
			new UiStack
			{
				Key = "iconRow",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Children =
				[
					new UiWhen
					{
						Key = "iconGate",
						Condition = () => face.Value.Icon is not null,
						Content = () => new UiImage
						{
							Key = "icon",
							Source = UiValue.From(() => face.Value.Icon!),
							Size = style.IconSize,
							Opacity = UiValue.From(() => face.Value.IsDimmed ? 0.45 : 1d),
							Saturation = UiValue.From(() => face.Value.IsDimmed ? 0d : 1d)
						}
					},
					new UiWhen
					{
						Key = "initialGate",
						Condition = () => face.Value.Icon is null,
						Content = () => new UiTextRun
						{
							Key = "initial",
							Text = UiText.From(() => face.Value.Initial),
							// The letter stands in for the icon, so it follows the icon size.
							Size = style.IconSize * 0.6,
							Weight = UiComponentTextWeights.Bold,
							Role = style.Foreground is null
								? UiValue.From(() => face.Value.IsDimmed
									? UiComponentTextRoles.Muted
									: UiComponentTextRoles.Primary)
								: UiValue.None<string>(),
							Color = style.Foreground is { } foreground
								? UiValue.From(() => face.Value.IsDimmed ? foreground + MutedAlpha : foreground)
								: UiValue.None<string>(),
							Align = UiComponentAlignments.Center
						}
					}
				]
			},
			new UiWhen
			{
				Key = "notRunningGate",
				Condition = () => face.Value.State == TileState.NotRunning,
				Content = () => Status("notRunning", Strings.Face.NotRunning(), style)
			},
			new UiWhen
			{
				Key = "notInstalledGate",
				Condition = () => face.Value.State == TileState.NotInstalled,
				Content = () => Status("notInstalled", Strings.Face.NotInstalled(), style)
			}
		];

		// A centred label is drawn over the icon in its own layer instead.
		if (style.LabelPosition != TileStyle.Center)
		{
			children.Insert(style.LabelPosition == TileStyle.Top ? 0 : 1, CaptionGate(face, style));
		}

		return new UiStack
		{
			Key = "body",
			Fill = true,
			Padding = safeArea,
			Direction = UiComponentDirections.Vertical,
			// A label on top sits low enough to clear the badge in the corner.
			Justify = style.LabelPosition == TileStyle.Top ? UiComponentJustify.End : UiComponentJustify.Center,
			Align = UiComponentAlignments.Stretch,
			Gap = 0.03,
			Children = children
		};
	}

	private static UiStack CenteredCaption(UiState<BadgeFace> face, UiSize safeArea, TileStyle style)
	{
		return new UiStack
		{
			Key = "centerCaption",
			Fill = true,
			Padding = safeArea,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Stretch,
			Children = [CaptionGate(face, style, shadow: true)]
		};
	}

	// Over the icon the label needs a shadow to stay readable, as on the Action Button.
	private static UiWhen CaptionGate(UiState<BadgeFace> face, TileStyle style, bool shadow = false)
	{
		return new UiWhen
		{
			Key = "captionGate",
			Condition = () => face.Value.Caption is not null,
			Content = () => new UiStack
			{
				Key = "captionRow",
				Direction = UiComponentDirections.Horizontal,
				Justify = style.Justify,
				Children =
				[
					new UiTextRun
					{
						Key = "caption",
						Text = UiText.From(() => face.Value.Caption),
						Size = style.CaptionSize,
						MinSize = Math.Min(style.CaptionSize, 0.055),
						Weight = shadow ? UiComponentTextWeights.Bold : UiComponentTextWeights.Medium,
						Role = Role(style, style.LabelColor is null ? UiComponentTextRoles.Secondary : null),
						Color = style.LabelColor is { } color ? UiValue.Of(color) : Tinted(style, SecondaryAlpha),
						FontFace = style.FontFace is { } font ? UiValue.Of(font) : UiValue.None<string>(),
						Align = style.Align,
						Shadow = shadow,
						MaxLines = 2
					}
				]
			}
		};
	}

	// One gate per badge width: a badge only resizes when its digit count changes, which rebuilds that gate.
	private static UiStack BadgeCorner(UiState<BadgeFace> face, UiSize safeArea, TileStyle style, BadgeOptions badge)
	{
		return new UiStack
		{
			Key = "badgeCorner",
			Fill = true,
			Padding = safeArea,
			Direction = UiComponentDirections.Vertical,
			Justify = badge.IsTop ? UiComponentJustify.Start : UiComponentJustify.End,
			Align = badge.IsRight ? UiComponentAlignments.End : UiComponentAlignments.Start,
			Children =
			[
				Badge(face, style.BadgeColor, badge, width: 1, basis: 0.14),
				Badge(face, style.BadgeColor, badge, width: 2, basis: 0.24),
				Badge(face, style.BadgeColor, badge, width: 3, basis: 0.31),
				Badge(face, style.BadgeColor, badge, width: 4, basis: 0.4)
			]
		};
	}

	private static UiWhen Badge(UiState<BadgeFace> face, string color, BadgeOptions badge, int width, double basis)
	{
		var height = (width == 1 ? 0.14 : 0.24) * badge.Scale;
		var outline = badge.Style == BadgeOptions.Outline;

		// An outline badge takes its number from the ring colour unless the user picked a text colour.
		var textColor = outline && string.Equals(badge.TextColor, BadgeOptions.DefaultTextColor,
			StringComparison.OrdinalIgnoreCase)
			? color
			: badge.TextColor;

		return new UiWhen
		{
			Key = $"badgeGate{width}",
			// The dot style keeps the badge at its smallest whatever the count.
			Condition = () => (badge.ShowsNumber ? face.Value.BadgeWidth : Math.Min(face.Value.BadgeWidth, 1)) == width,
			Content = () => new UiStack
			{
				Key = $"badgeRow{width}",
				MainSize = UiSize.FromBasis(height),
				Direction = UiComponentDirections.Horizontal,
				Justify = badge.IsRight ? UiComponentJustify.End : UiComponentJustify.Start,
				Children =
				[
					new UiLayer
					{
						Key = $"badge{width}",
						MainSize = UiSize.FromBasis(basis * badge.Scale),
						Children =
						[
							new UiShape
							{
								Key = $"badgeShape{width}",
								Fill = true,
								Shape = badge.ShapeFor(width),
								CornerRadius = badge.Style == BadgeOptions.Square ? 0.04 * badge.Scale : UiSize.None(),
								Color = outline ? BadgeOptions.OutlineFill : color,
								StrokeColor = outline ? UiValue.Of(color) : UiValue.None<string>(),
								StrokeWidth = outline ? 0.018 * badge.Scale : UiSize.None()
							},
							new UiStack
							{
								Key = $"badgeLabel{width}",
								Fill = true,
								Justify = UiComponentJustify.Center,
								Align = UiComponentAlignments.Center,
								Children =
								[
									new UiTextRun
									{
										Key = $"badgeText{width}",
										Text = UiText.From(() => badge.ShowsNumber ? face.Value.Badge : string.Empty),
										Size = 0.15 * badge.Scale,
										MinSize = 0.1 * badge.Scale,
										Weight = UiComponentTextWeights.Bold,
										Color = textColor,
										Align = UiComponentAlignments.Center
									}
								]
							}
						]
					}
				]
			}
		};
	}

	private static UiStack Unconfigured(UiSize safeArea, TileStyle style)
	{
		return new UiStack
		{
			Key = "unconfigured",
			Fill = true,
			Padding = safeArea,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = 0.02,
			Children =
			[
				new UiTextRun
				{
					Key = "chooseTitle",
					Text = Strings.Face.ChooseApp(),
					Size = 0.1,
					MinSize = 0.07,
					Weight = UiComponentTextWeights.SemiBold,
					Role = Role(style, UiComponentTextRoles.Secondary),
					Color = Tinted(style, SecondaryAlpha),
					Align = UiComponentAlignments.Center,
					MaxLines = 2
				},
				new UiTextRun
				{
					Key = "chooseHint",
					Text = Strings.Face.ChooseAppHint(),
					Size = 0.07,
					MinSize = 0.05,
					Role = Role(style, UiComponentTextRoles.Muted),
					Color = Tinted(style, MutedAlpha),
					Align = UiComponentAlignments.Center,
					MaxLines = 2
				}
			]
		};
	}

	private static UiTextRun Status(string key, UiText text, TileStyle style)
	{
		return new UiTextRun
		{
			Key = key,
			Text = text,
			Size = 0.075,
			MinSize = 0.055,
			Weight = UiComponentTextWeights.Medium,
			Role = Role(style, UiComponentTextRoles.Muted),
			Color = Tinted(style, MutedAlpha),
			Align = UiComponentAlignments.Center,
			MaxLines = 1
		};
	}

	// The theme's text role, unless a custom background picked a foreground colour instead.
	private static UiValue<string> Role(TileStyle style, string? role)
	{
		return style.Foreground is null && role is not null ? UiValue.Of(role) : UiValue.None<string>();
	}

	private static UiValue<string> Tinted(TileStyle style, string alpha)
	{
		return style.Foreground is { } foreground ? UiValue.Of(foreground + alpha) : UiValue.None<string>();
	}
}
