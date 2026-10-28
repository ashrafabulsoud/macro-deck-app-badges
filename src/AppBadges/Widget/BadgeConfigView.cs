using System.Text.Json;
using AppBadges.Badges;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace AppBadges.Widget;

/// <summary>The widget's configuration form. Each input's key is the data key it writes.</summary>
internal static class BadgeConfigView
{
	public const string BundleIdKey = "bundleId";
	public const string IconSizeKey = "iconSize";

	public const double DefaultIconSize = 70;
	public const double MinIconSize = 30;
	public const double MaxIconSize = 90;

	// Everything the Action Button offers that a provider's type can have; the accent colour paints the badge.
	public const UiWidgetAppearanceFields AppearanceFields = UiWidgetAppearanceFields.All
		| UiWidgetAppearanceFields.TransparentBackground;

	public static UiElement Build(JsonElement data, IReadOnlyList<InstalledApp> apps)
	{
		ArgumentNullException.ThrowIfNull(apps);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiChoiceInput
					{
						Key = BundleIdKey,
						Label = Strings.Config.App(),
						Description = Strings.Config.AppDescription(),
						Searchable = true,
						Binding = Bind.To(new UiState<string>(ReadBundleId(data) ?? string.Empty)),
						Options = UiValue.Of(Options(apps))
					},
					new UiNumberInput
					{
						Key = IconSizeKey,
						Label = Strings.Config.IconSize(),
						Description = Strings.Config.IconSizeDescription(),
						Min = MinIconSize,
						Max = MaxIconSize,
						ShowSlider = true,
						Binding = Bind.To(new UiState<double>(ReadIconSize(data)))
					},
					.. BadgeSection(data),
					Described(UiWidgetAppearance.Section(data, AppearanceFields))
				]
			}
		};
	}

	public static string? ReadBundleId(JsonElement data)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(BundleIdKey, out var value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	private static IEnumerable<UiElement> BadgeSection(JsonElement data)
	{
		var badge = BadgeOptions.Read(data);

		yield return new UiHeading { Key = "badgeHeading", Text = Strings.Config.BadgeHeading() };
		yield return new UiChoiceInput
		{
			Key = BadgeOptions.PositionKey,
			Label = Strings.Config.BadgePosition(),
			Description = Strings.Config.BadgePositionDescription(),
			Binding = Bind.To(new UiState<string>(badge.Position)),
			Options = UiValue.Of<IReadOnlyList<UiOption>>(
			[
				UiOption.Of(BadgeOptions.TopRight, Strings.Position.TopRight()),
				UiOption.Of(BadgeOptions.TopLeft, Strings.Position.TopLeft()),
				UiOption.Of(BadgeOptions.BottomRight, Strings.Position.BottomRight()),
				UiOption.Of(BadgeOptions.BottomLeft, Strings.Position.BottomLeft())
			])
		};
		yield return new UiChoiceInput
		{
			Key = BadgeOptions.StyleKey,
			Label = Strings.Config.BadgeStyle(),
			Description = Strings.Config.BadgeStyleDescription(),
			Binding = Bind.To(new UiState<string>(badge.Style)),
			Options = UiValue.Of<IReadOnlyList<UiOption>>(
			[
				UiOption.Of(BadgeOptions.Pill, Strings.Style.Pill()),
				UiOption.Of(BadgeOptions.Square, Strings.Style.Square()),
				UiOption.Of(BadgeOptions.Outline, Strings.Style.Outline()),
				UiOption.Of(BadgeOptions.Dot, Strings.Style.Dot())
			])
		};
		yield return new UiNumberInput
		{
			Key = BadgeOptions.SizeKey,
			Label = Strings.Config.BadgeSize(),
			Description = Strings.Config.BadgeSizeDescription(),
			Min = BadgeOptions.MinSize,
			Max = BadgeOptions.MaxSize,
			ShowSlider = true,
			Binding = Bind.To(new UiState<double>(badge.Scale * 100))
		};
		yield return new UiColorInput
		{
			Key = BadgeOptions.TextColorKey,
			Label = Strings.Config.BadgeTextColor(),
			Description = Strings.Config.BadgeTextColorDescription(),
			Binding = Bind.To(new UiState<string>(badge.TextColor))
		};
	}

	// The standard appearance fields come without descriptions; each one gets its own here.
	internal static UiFragment Described(UiFragment section)
	{
		return section with { Children = [.. section.Children.Select(Describe)] };
	}

	private static UiElement Describe(UiElement element)
	{
		var description = element.Key switch
		{
			UiWidgetAppearanceKeys.Label => Strings.Describe.Label(),
			UiWidgetAppearanceKeys.BackgroundColor => Strings.Describe.BackgroundColor(),
			UiWidgetAppearanceKeys.LabelColor => Strings.Describe.LabelColor(),
			UiWidgetAppearanceKeys.AccentColor => Strings.Describe.AccentColor(),
			UiWidgetAppearanceKeys.FontFaceId => Strings.Describe.Font(),
			UiWidgetAppearanceKeys.FontSize => Strings.Describe.FontSize(),
			UiWidgetAppearanceKeys.TextAlign => Strings.Describe.TextAlign(),
			UiWidgetAppearanceKeys.LabelPosition => Strings.Describe.LabelPosition(),
			UiWidgetAppearanceKeys.Border => Strings.Describe.Border(),
			UiWidgetAppearanceKeys.BorderStyle => Strings.Describe.BorderStyle(),
			UiWidgetAppearanceKeys.BorderColor => Strings.Describe.BorderColor(),
			_ => (UiText?)null
		};

		return element switch
		{
			UiObjectInput border => border with
			{
				Description = description ?? border.Description,
				Children = [.. border.Children.Select(Describe)]
			},
			UiInput<string> input when description is { } text => input with { Description = text },
			UiInput<double> input when description is { } text => input with { Description = text },
			_ => element
		};
	}

	/// <summary>The icon size in percent of the tile; a missing or out-of-range value reads as the default or is clamped.</summary>
	public static double ReadIconSize(JsonElement data)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(IconSizeKey, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetDouble(out var size)
			&& double.IsFinite(size)
				? Math.Clamp(size, MinIconSize, MaxIconSize)
				: DefaultIconSize;
	}

	// Two apps can share a name (an app and its beta); the bundle id tells them apart.
	internal static IReadOnlyList<UiOption> Options(IReadOnlyList<InstalledApp> apps)
	{
		var shared = apps.GroupBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToHashSet(StringComparer.CurrentCultureIgnoreCase);

		return
		[
			.. apps.Select(app => UiOption.Of(app.BundleId,
				shared.Contains(app.Name) ? $"{app.Name} ({app.BundleId})" : app.Name))
		];
	}
}
