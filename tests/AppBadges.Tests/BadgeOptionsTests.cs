using System.Text.Json;
using AppBadges.Widget;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using NUnit.Framework;

namespace AppBadges.Tests;

[TestFixture]
public sealed class BadgeOptionsTests
{
	[Test]
	public void Without_settings_the_badge_looks_like_the_docks()
	{
		Assert.That(Read("{}"), Is.EqualTo(BadgeOptions.Default));
		Assert.That(BadgeOptions.Default.IsTop && BadgeOptions.Default.IsRight, Is.True);
	}

	[Test]
	public void Stored_settings_are_read()
	{
		var badge = Read("""
			{ "badgePosition": "bottom-left", "badgeSize": 150, "badgeStyle": "outline", "badgeTextColor": "#000000" }
			""");

		Assert.That(badge, Is.EqualTo(new BadgeOptions(BadgeOptions.BottomLeft, 1.5, BadgeOptions.Outline, "#000000")));
		Assert.That(badge.IsTop, Is.False);
		Assert.That(badge.IsRight, Is.False);
	}

	[Test]
	public void Unknown_or_out_of_range_values_fall_back()
	{
		var badge = Read("""{ "badgePosition": "middle", "badgeSize": 900, "badgeStyle": "star", "badgeTextColor": " " }""");

		Assert.That(badge, Is.EqualTo(new BadgeOptions(BadgeOptions.TopRight, 2, BadgeOptions.Pill,
			BadgeOptions.DefaultTextColor)));
	}

	[Test]
	public void The_dot_style_hides_the_number()
	{
		Assert.That(Read("""{ "badgeStyle": "dot" }""").ShowsNumber, Is.False);
		Assert.That(Read("{}").ShowsNumber, Is.True);
	}

	[Test]
	public void The_square_style_uses_a_rounded_rectangle()
	{
		var square = Read("""{ "badgeStyle": "square" }""");

		Assert.That(square.ShapeFor(1), Is.EqualTo(UiComponentShapes.RoundedRectangle));
		Assert.That(BadgeOptions.Default.ShapeFor(1), Is.EqualTo(UiComponentShapes.Circle));
		Assert.That(BadgeOptions.Default.ShapeFor(3), Is.EqualTo(UiComponentShapes.Capsule));
	}

	[Test]
	public void Every_standard_appearance_field_gets_a_description()
	{
		using var document = JsonDocument.Parse("{}");
		var section = BadgeConfigView.Described(
			UiWidgetAppearance.Section(document.RootElement, BadgeConfigView.AppearanceFields));

		var inputs = Inputs(section.Children).ToList();

		// Checking the visited keys keeps this from passing vacuously if the SDK reshapes the section.
		Assert.That(inputs.Select(input => input.Key), Is.EquivalentTo(new[]
		{
			"label", "backgroundColor", "labelColor", "accentColor", "fontFaceId", "fontSize", "textAlign",
			"labelPosition", "border", "style", "color"
		}));
		Assert.That(inputs.Where(input => !input.Described).Select(input => input.Key), Is.Empty);
	}

	private static IEnumerable<(string Key, bool Described)> Inputs(IEnumerable<UiElement> elements)
	{
		foreach (var element in elements)
		{
			switch (element)
			{
				case UiObjectInput container:
					yield return (container.Key, container.Description.IsDeclared);

					foreach (var child in Inputs(container.Children))
					{
						yield return child;
					}

					break;
				case UiInput<string> input:
					yield return (input.Key, input.Description.IsDeclared);
					break;
				case UiInput<double> input:
					yield return (input.Key, input.Description.IsDeclared);
					break;
			}
		}
	}

	private static BadgeOptions Read(string json)
	{
		using var document = JsonDocument.Parse(json);
		return BadgeOptions.Read(document.RootElement.Clone());
	}
}
