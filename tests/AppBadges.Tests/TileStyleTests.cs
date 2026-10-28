using System.Text.Json;
using AppBadges.Widget;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using NUnit.Framework;

namespace AppBadges.Tests;

[TestFixture]
public sealed class TileStyleTests
{
	[Test]
	public void Without_settings_the_tile_keeps_its_defaults()
	{
		var style = Read("{}");

		Assert.That(style, Is.EqualTo(new TileStyle(null, TileStyle.DefaultCaptionSize, null, "center",
			TileStyle.Bottom, TileStyle.DefaultBadgeColor, 0.7)));
	}

	[Test]
	public void The_standard_settings_are_applied_like_the_action_button()
	{
		var style = Read("""
			{ "labelColor": "#ffd60a", "fontFaceId": "inter-bold", "fontSize": 12, "textAlign": "left",
			  "labelPosition": "top", "accentColor": "#0a84ff" }
			""");

		Assert.That(style, Is.EqualTo(new TileStyle("#ffd60a", 0.12, "inter-bold", "left", TileStyle.Top, "#0a84ff", 0.7)));
		Assert.That(style.Justify, Is.EqualTo(UiComponentJustify.Start));
		Assert.That(style.Align, Is.EqualTo(UiComponentAlignments.Start));
	}

	[TestCase(1, 0.04)]
	[TestCase(500, 0.4)]
	public void A_font_size_out_of_range_is_clamped(double percent, double expected)
	{
		Assert.That(Read($$"""{ "fontSize": {{percent}} }""").CaptionSize, Is.EqualTo(expected));
	}

	[Test]
	public void Unknown_alignment_and_position_fall_back_to_the_defaults()
	{
		var style = Read("""{ "textAlign": "justify", "labelPosition": "sideways", "accentColor": "  " }""");

		Assert.That(style.TextAlign, Is.EqualTo("center"));
		Assert.That(style.LabelPosition, Is.EqualTo(TileStyle.Bottom));
		Assert.That(style.BadgeColor, Is.EqualTo(TileStyle.DefaultBadgeColor));
	}

	[TestCase("{}", 70)]
	[TestCase("""{ "iconSize": 55 }""", 55)]
	[TestCase("""{ "iconSize": 5 }""", 30)]
	[TestCase("""{ "iconSize": 400 }""", 90)]
	[TestCase("""{ "iconSize": "big" }""", 70)]
	public void The_icon_size_is_read_from_the_widget_data(string json, double expected)
	{
		using var document = JsonDocument.Parse(json);

		Assert.That(BadgeConfigView.ReadIconSize(document.RootElement), Is.EqualTo(expected));
	}

	[TestCase("#3a1c4a", "#ffffff")]
	[TestCase("#ffd60a", "#1c1c1e")]
	[TestCase("#ffffffff", "#1c1c1e")]
	[TestCase("#00000020", null)]
	[TestCase("transparent", null)]
	[TestCase("#zzzzzz", null)]
	[TestCase(null, null)]
	public void A_custom_background_picks_readable_text(string? background, string? expected)
	{
		Assert.That(TileStyle.ForegroundOn(background), Is.EqualTo(expected));
	}

	private static TileStyle Read(string json)
	{
		using var document = JsonDocument.Parse(json);
		return TileStyle.From(UiWidgetAppearance.Read(document.RootElement.Clone()));
	}
}
