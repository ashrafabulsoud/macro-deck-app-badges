using AppBadges.Badges;
using AppBadges.Platform;
using NUnit.Framework;

namespace AppBadges.Tests;

[TestFixture]
public sealed class BadgeParsingTests
{
	private const string RunningWithBadge = """
		"Slack" ASN:0x0-0x45a45a:
		    "CFBundleIdentifier"="com.tinyspeck.slackmacgap"
		    "StatusLabel"={ "label"="17" }
		""";

	[Test]
	public void A_running_app_with_a_badge_reports_its_label()
	{
		Assert.That(StatusLabelParser.Parse(RunningWithBadge), Is.EqualTo(new AppBadge(true, "17")));
	}

	[Test]
	public void A_running_app_without_a_badge_has_no_label()
	{
		var badge = StatusLabelParser.Parse("\"WhatsApp\" ASN:0x0-0x3596593:\n    \"ApplicationType\"=\"Foreground\"");

		Assert.That(badge, Is.EqualTo(new AppBadge(true, null)));
		Assert.That(badge.HasBadge, Is.False);
	}

	[Test]
	public void No_output_means_the_app_is_not_running()
	{
		Assert.That(StatusLabelParser.Parse("  \n"), Is.EqualTo(AppBadge.NotRunning));
	}

	[Test]
	public void An_empty_label_is_a_badge_without_a_count()
	{
		var badge = StatusLabelParser.Parse("\"X\" ASN:1:\n \"StatusLabel\"={ \"label\"=\"\" }");

		Assert.That(badge.HasBadge, Is.True);
		Assert.That(BadgeText.Format(badge.Label!), Is.Empty);
	}

	[TestCase("3", "3")]
	[TestCase("99", "99")]
	[TestCase("100", "99+")]
	[TestCase(" 7 ", "7")]
	[TestCase("New", "New")]
	[TestCase("Updates", "Upd…")]
	public void Badge_text_is_shortened_like_the_dock(string label, string shown)
	{
		Assert.That(BadgeText.Format(label), Is.EqualTo(shown));
	}

	[TestCase("com.tinyspeck.slackmacgap", true)]
	[TestCase("net.whatsapp.WhatsApp", true)]
	[TestCase("-a", false)]
	[TestCase("--args", false)]
	[TestCase("../../etc", false)]
	[TestCase("com.example/app", false)]
	[TestCase("com example", false)]
	[TestCase("", false)]
	[TestCase(null, false)]
	public void Only_plain_bundle_ids_are_accepted(string? bundleId, bool valid)
	{
		Assert.That(BundleIds.IsValid(bundleId), Is.EqualTo(valid));
	}
}
