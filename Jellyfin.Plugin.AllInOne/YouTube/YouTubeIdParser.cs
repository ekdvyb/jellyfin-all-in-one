using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AllInOne.YouTube;

/// <summary>
/// Extracts YouTube video ids from URLs, filenames and sidecar text.
/// </summary>
public static partial class YouTubeIdParser
{
    [GeneratedRegex(
        @"(?:youtube\.com/(?:watch\?(?:[^#\s]*?&)?v=|embed/|shorts/|live/)|youtu\.be/|youtube:)([A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(
        @"(?:youtube(?:\s*(?:id|video))?|yt)\s*[:=\-#]?\s*\[?([A-Za-z0-9_-]{11})\]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaggedPattern();

    [GeneratedRegex(@"[\[\(\{]([A-Za-z0-9_-]{11})[\]\)\}]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketPattern();

    [GeneratedRegex(@"^([A-Za-z0-9_-]{11})(?:\s+[-–—\[(]|\s*$)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex StandalonePattern();

    /// <summary>
    /// Tries to find a YouTube video id in the supplied text.
    /// </summary>
    /// <param name="input">Text to search.</param>
    /// <returns>The 11-character video id, or <c>null</c> when nothing was found.</returns>
    public static string? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = UrlPattern().Match(input);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        match = TaggedPattern().Match(input);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        match = BracketPattern().Match(input);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        var trimmed = input.Trim();
        if (StandalonePattern().IsMatch(trimmed))
        {
            return trimmed;
        }

        match = LeadingIdPattern().Match(trimmed);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Checks whether a string looks like a YouTube video id.
    /// </summary>
    /// <param name="value">Candidate value.</param>
    /// <returns><c>true</c> when valid.</returns>
    public static bool IsValidId(string? value)
        => value is { Length: 11 } && StandalonePattern().IsMatch(value);
}
