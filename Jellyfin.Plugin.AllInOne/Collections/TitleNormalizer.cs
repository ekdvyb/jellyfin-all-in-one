using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AllInOne.Collections;

/// <summary>
/// Normalises media titles so sequels and alternate cuts can be recognised as one franchise.
/// </summary>
public static partial class TitleNormalizer
{
    private static readonly string[] Articles = ["the", "a", "an"];
    private static readonly string[] sequelMarkers =
    [
        "part", "pt", "chapter", "ch", "episode", "ep", "volume", "vol",
        "number", "no", "season", "s", "series", "film", "movie"
    ];

    /// <summary>
    /// Produces a comparison key for exact/near-exact duplicate titles.
    /// </summary>
    public static string Key(string? title)
        => string.Join(' ', Tokenize(title, stripSequelNumbers: true));

    /// <summary>
    /// Produces the token sequence used for franchise detection.
    /// </summary>
    public static string[] FranchiseTokens(string? title)
        => Tokenize(title, stripSequelNumbers: true);

    private static string[] Tokenize(string? title, bool stripSequelNumbers)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var cleaned = BracketTextRegex().Replace(title, " ");
        cleaned = cleaned.Replace('&', ' ');
        cleaned = cleaned.Replace("\u2019", "'");
        cleaned = NonWordRegex().Replace(cleaned, " ");

        var tokens = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('\''))
            .Where(t => t.Length > 0)
            .ToList();

        // Drop leading articles: "The Matrix" and "Matrix Reloaded" must match.
        while (tokens.Count > 0 && Articles.Contains(tokens[0], StringComparer.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(0);
        }

        // Articles inside a title are noise for grouping ("Pirates of the Caribbean").
        tokens = tokens
            .Where(t => !Articles.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (stripSequelNumbers)
        {
            TrimSequel(tokens);
        }

        return tokens.Select(t => t.ToLowerInvariant()).ToArray();
    }

    private static void TrimSequel(List<string> tokens)
    {
        while (tokens.Count > 0)
        {
            var last = tokens[^1];

            if (IsOrdinal(last))
            {
                tokens.RemoveAt(tokens.Count - 1);
                if (tokens.Count > 0 && SequelMarkerNeedsPrevious(tokens[^1]))
                {
                    tokens.RemoveAt(tokens.Count - 1);
                }

                continue;
            }

            if (sequelMarkers.Contains(last, StringComparer.OrdinalIgnoreCase))
            {
                tokens.RemoveAt(tokens.Count - 1);
                continue;
            }

            break;
        }
    }

    private static bool SequelMarkerNeedsPrevious(string token)
        => sequelMarkers.Contains(token, StringComparer.OrdinalIgnoreCase)
           || token.Equals("part", StringComparison.OrdinalIgnoreCase);

    private static bool IsOrdinal(string token)
    {
        if (int.TryParse(token, out _))
        {
            return true;
        }

        return RomanNumerals.Contains(token, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] RomanNumerals =
    [
        "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x",
        "xi", "xii", "xiii", "xiv", "xv", "xvi", "xvii", "xviii", "xix", "xx",
        "xxi", "xxx", "xl", "l"
    ];

    [GeneratedRegex(@"\s*[\[\(\{][^\]\)\}]*[\]\)\}]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex BracketTextRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordRegex();
}
