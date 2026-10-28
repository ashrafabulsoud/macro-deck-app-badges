using AppBadges.Badges;
using MacroDeck.Ui.Model.Resources;

namespace AppBadges.Widget;

internal enum TileState
{
	Unconfigured,
	NotInstalled,
	NotRunning,
	Running
}

/// <summary>What a tile shows. Equal faces produce no patch.</summary>
internal sealed record BadgeFace(TileState State, string Name, UiResource? Icon, string? Badge, string? Caption)
{
	public bool IsConfigured => State != TileState.Unconfigured;

	public bool IsDimmed => State is TileState.NotRunning or TileState.NotInstalled;

	public string Initial => State != TileState.NotInstalled && Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

	/// <summary>0 for no badge, 1 for a dot, otherwise one more than the characters it holds (2 to 4).</summary>
	public int BadgeWidth => Badge switch
	{
		null => 0,
		"" => 1,
		_ => Math.Clamp(Badge.Length, 1, 3) + 1
	};

	public static BadgeFace Unconfigured(string? caption)
	{
		return new BadgeFace(TileState.Unconfigured, string.Empty, null, null, caption);
	}

	public static BadgeFace For(InstalledApp? app, string bundleId, UiResource? icon, AppBadge? badge, string? caption)
	{
		if (app is null)
		{
			return new BadgeFace(TileState.NotInstalled, bundleId, null, null, caption);
		}

		var running = badge is { IsRunning: true };

		return new BadgeFace(
			running ? TileState.Running : TileState.NotRunning,
			app.Name,
			icon,
			badge is { HasBadge: true, Label: { } label } ? BadgeText.Format(label) : null,
			caption);
	}
}
