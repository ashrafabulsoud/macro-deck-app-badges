using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;

namespace AppBadges.Widget;

// Previews draw the first letter instead of an icon: a scenario cannot register resources.
internal static class BadgeWidgetViewPreviews
{
	[UiPreview("Badge with a count", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Count() => Build(new BadgeFace(TileState.Running, "Slack", null, "17", null));

	[UiPreview("Single digit", Profile = UiPreviewProfiles.Widget)]
	public static UiElement SingleDigit() => Build(new BadgeFace(TileState.Running, "WhatsApp", null, "3", null));

	[UiPreview("Over 99", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Many() => Build(new BadgeFace(TileState.Running, "Discord", null, "99+", null));

	[UiPreview("Dot", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Dot() => Build(new BadgeFace(TileState.Running, "Telegram", null, "", null));

	[UiPreview("No unread", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Quiet() => Build(new BadgeFace(TileState.Running, "Messages", null, null, null));

	[UiPreview("Not running", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NotRunning() => Build(new BadgeFace(TileState.NotRunning, "Mail", null, null, null));

	[UiPreview("With label", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Labelled() => Build(new BadgeFace(TileState.Running, "Slack", null, "5", "Work Slack"));

	[UiPreview("Styled label on top", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StyledTop() => Build(new BadgeFace(TileState.Running, "Slack", null, "17", "Work"),
		"""{"label":"Work","labelColor":"#ffd60a","fontSize":12,"labelPosition":"top","backgroundColor":"#3a1c4a"}""");

	[UiPreview("Accent badge, label left", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Accent() => Build(new BadgeFace(TileState.Running, "Discord", null, "8", "Gaming"),
		"""{"label":"Gaming","accentColor":"#0a84ff","textAlign":"left"}""");

	[UiPreview("Label in the center", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Centered() => Build(new BadgeFace(TileState.Running, "Mail", null, "2", "Inbox"),
		"""{"label":"Inbox","labelPosition":"center","fontSize":16,"labelColor":"#ffffff"}""");

	[UiPreview("Small icon with label", Profile = UiPreviewProfiles.Widget)]
	public static UiElement SmallIcon() => Build(new BadgeFace(TileState.Running, "Slack", null, "4", "Team"),
		"""{"label":"Team","iconSize":45,"fontSize":14}""");

	[UiPreview("Big badge bottom left", Profile = UiPreviewProfiles.Widget)]
	public static UiElement BigBottomLeft() => Build(new BadgeFace(TileState.Running, "Slack", null, "17", null),
		"""{"badgePosition":"bottom-left","badgeSize":150}""");

	[UiPreview("Outline badge", Profile = UiPreviewProfiles.Widget)]
	public static UiElement OutlineBadge() => Build(new BadgeFace(TileState.Running, "Discord", null, "18", null),
		"""{"badgeStyle":"outline","accentColor":"#5865f2"}""");

	[UiPreview("Square badge", Profile = UiPreviewProfiles.Widget)]
	public static UiElement SquareBadge() => Build(new BadgeFace(TileState.Running, "Telegram", null, "6", null),
		"""{"badgeStyle":"square","badgeTextColor":"#000000","accentColor":"#ffd60a"}""");

	[UiPreview("Dot only", Profile = UiPreviewProfiles.Widget)]
	public static UiElement DotOnly() => Build(new BadgeFace(TileState.Running, "WhatsApp", null, "12", null),
		"""{"badgeStyle":"dot","badgeSize":130,"accentColor":"#34c759"}""");

	[UiPreview("Not installed", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Missing() => Build(new BadgeFace(TileState.NotInstalled, "com.example.gone", null, null, null));

	[UiPreview("Not configured", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Unconfigured() => Build(BadgeFace.Unconfigured(null));

	private static UiElement Build(BadgeFace face, string appearance = "{}")
	{
		using var document = JsonDocument.Parse(appearance);

		return BadgeWidgetView.Build(new UiState<BadgeFace>(face), document.RootElement.Clone(),
			UiWidgetSafeArea.DefaultCornerRadius);
	}
}
