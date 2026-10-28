using System.Text.RegularExpressions;

namespace AppBadges.Badges;

/// <summary>Parses the output of <c>lsappinfo info -long -app &lt;bundle id&gt;</c>.</summary>
public static partial class StatusLabelParser
{
	public static AppBadge Parse(string output)
	{
		ArgumentNullException.ThrowIfNull(output);

		// lsappinfo prints nothing for an app that is not running.
		if (string.IsNullOrWhiteSpace(output))
		{
			return AppBadge.NotRunning;
		}

		var match = LabelPattern().Match(output);

		return new AppBadge(true, match.Success ? match.Groups["label"].Value : null);
	}

	[GeneratedRegex("\"StatusLabel\"=\\{\\s*\"label\"=\"(?<label>[^\"]*)\"")]
	private static partial Regex LabelPattern();
}
