using System.Text.RegularExpressions;
using Jellyfin.Plugin.AllInOne.Configuration;
using Jellyfin.Plugin.AllInOne.YouTube;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.AllInOne.Collections;

/// <summary>
/// Turns a library snapshot into the set of collections the plugin wants to own.
/// </summary>
public static partial class SmartGroupBuilder
{
    private static readonly char[] InvalidNameChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Builds all enabled collections. Jellyfin picks the matching build for its server version.
    /// </summary>
    public static Dictionary<string, List<Guid>> Build(
        IReadOnlyList<BaseItem> items,
        PluginConfiguration config)
    {
        var result = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        var minimum = Math.Max(1, config.MinItemsPerCollection);
        var prefix = config.CollectionPrefix ?? string.Empty;

        void Add(string category, string value, IEnumerable<BaseItem> group)
        {
            var list = group
                .GroupBy(item => item.Id)
                .Select(g => g.First())
                .ToList();

            if (list.Count < minimum || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            result[SanitizeName($"{prefix}{category} - {value}")] = list.Select(i => i.Id).ToList();
        }

        var movies = items.OfType<Movie>().ToList();
        var shows = items.OfType<Series>().ToList();
        var episodes = items.OfType<Episode>().ToList();
        var musicVideos = items.OfType<MusicVideo>().ToList();
        var otherVideos = items
            .Where(i => i is Video && i is not Movie && i is not MusicVideo)
            .ToList();

        var youtubeIds = items
            .Where(i => YouTubeMetadataService.FindVideoId(i) is not null)
            .Select(i => i.Id)
            .ToHashSet();
        var youtubeItems = items.Where(i => youtubeIds.Contains(i.Id)).ToList();
        var nonYouTube = items.Where(i => !youtubeIds.Contains(i.Id)).ToList();

        if (config.GroupByType)
        {
            Add("Type", "Movies", movies);
            Add("Type", "TV Shows", shows);
            Add("Type", "TV Episodes", episodes);
            Add("Type", "Music Videos", musicVideos);
            Add("Type", "YouTube Videos", youtubeItems);
            Add("Type", "Other Videos", otherVideos.Where(i => !youtubeIds.Contains(i.Id)));
        }

        if (config.GroupByFranchise)
        {
            AddFranchises(items, config, Add);
        }

        if (config.GroupByTitle)
        {
            foreach (var group in items
                         .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                         .GroupBy(i => TitleNormalizer.Key(i.Name), StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Key.Length > 0))
            {
                Add("Name", group.First().Name ?? group.Key, group);
            }
        }

        if (config.GroupBySeries)
        {
            foreach (var group in episodes
                         .GroupBy(i => i.SeriesId != Guid.Empty ? i.SeriesId.ToString() : (i.SeriesName ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                         .Where(g => !string.IsNullOrWhiteSpace(g.Key)))
            {
                var name = group.First().SeriesName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    Add("Series", name!, group);
                }
            }
        }

        if (config.GroupByStudio)
        {
            var studioLookup = nonYouTube
                .SelectMany(i => (i.Studios ?? [])
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => new { Studio = s.Trim(), Item = i }))
                .ToLookup(x => x.Studio, x => x.Item, StringComparer.OrdinalIgnoreCase);

            foreach (var group in studioLookup)
            {
                Add("Studio", group.Key, group);
            }
        }

        if (config.GroupByGenre)
        {
            var genreLookup = items
                .SelectMany(i => (i.Genres ?? [])
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .Select(g => new { Genre = g.Trim(), Item = i }))
                .ToLookup(x => x.Genre, x => x.Item, StringComparer.OrdinalIgnoreCase);

            foreach (var group in genreLookup)
            {
                Add("Genre", group.Key, group);
            }
        }

        if (config.GroupByYouTubeChannel)
        {
            var channelLookup = youtubeItems
                .Select(i => new { Channel = GetYouTubeChannel(i), Item = i })
                .Where(x => !string.IsNullOrWhiteSpace(x.Channel))
                .ToLookup(x => x.Channel!, x => x.Item, StringComparer.OrdinalIgnoreCase);

            foreach (var group in channelLookup)
            {
                Add("YouTube Channel", group.Key, group);
            }
        }

        if (config.GroupByDecade)
        {
            var decadeLookup = items
                .Select(i => new { Item = i, Year = i.ProductionYear ?? i.PremiereDate?.Year })
                .Where(x => x.Year.HasValue)
                .Select(x => new { x.Item, Decade = x.Year!.Value / 10 * 10 })
                .ToLookup(x => x.Decade, x => x.Item);

            foreach (var group in decadeLookup)
            {
                Add("Decade", $"{group.Key}s", group);
            }
        }

        return result;
    }

    private static void AddFranchises(
        IReadOnlyList<BaseItem> items,
        PluginConfiguration config,
        Action<string, string, IEnumerable<BaseItem>> add)
    {
        var candidates = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .ToList();

        if (candidates.Count < 2)
        {
            return;
        }

        var parent = Enumerable.Range(0, candidates.Count).ToArray();
        var tokens = candidates
            .Select(i => TitleNormalizer.FranchiseTokens(i.Name))
            .ToArray();

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        // Jellyfin's providers often expose a TmdbCollection/CollectionId even when the
        // titles give no clue ("Batman Begins" vs "The Dark Knight"), so honour that first.
        var byProvider = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < candidates.Count; i++)
        {
            foreach (var pair in candidates[i].ProviderIds)
            {
                if (pair.Key.Contains("collection", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(pair.Value)
                    && byProvider.TryGetValue(pair.Value.Trim(), out var other))
                {
                    Union(other, i);
                }
                else if (pair.Key.Contains("collection", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    byProvider[pair.Value.Trim()] = i;
                }
            }
        }

        // Compare only titles sharing a first word. This keeps the work close to O(n)
        // instead of comparing every movie with every other movie.
        foreach (var bucket in Enumerable.Range(0, candidates.Count)
                     .GroupBy(i => tokens[i].FirstOrDefault() ?? string.Empty))
        {
            var indexes = bucket.ToArray();
            for (var a = 0; a < indexes.Length; a++)
            {
                for (var b = a + 1; b < indexes.Length; b++)
                {
                    var left = tokens[indexes[a]];
                    var right = tokens[indexes[b]];
                    var common = CommonPrefix(left, right);
                    if (IsFranchisePrefix(common, left, right, config))
                    {
                        Union(indexes[a], indexes[b]);
                    }
                }
            }
        }

        foreach (var group in Enumerable.Range(0, candidates.Count).GroupBy(Find))
        {
            var indexes = group.ToList();
            if (indexes.Count < Math.Max(2, config.MinItemsPerCollection))
            {
                continue;
            }

            add("Franchise", FranchiseDisplayName(indexes, tokens, candidates), indexes.Select(i => candidates[i]));
        }
    }

    private static string FranchiseDisplayName(
        List<int> indexes,
        string[][] tokens,
        IReadOnlyList<BaseItem> candidates)
    {
        var common = tokens[indexes[0]].ToArray();
        foreach (var index in indexes.Skip(1))
        {
            common = CommonPrefix(common, tokens[index]);
        }

        while (common.Length > 0 && DisplayStopWords.Contains(common[^1], StringComparer.OrdinalIgnoreCase))
        {
            common = common[..^1];
        }

        if (common.Length > 0 && (common.Length > 1 || common[0].Length >= 4))
        {
            return TitleCase(common);
        }

        // Provider-based groups can have no shared title at all ("The Dark Knight" / "Batman Begins").
        var shortest = indexes
            .OrderBy(i => tokens[i].Length)
            .ThenBy(i => candidates[i].ProductionYear ?? 9999)
            .ThenBy(i => candidates[i].Name, StringComparer.OrdinalIgnoreCase)
            .First();

        return CleanDisplayTitle(candidates[shortest].Name);
    }

    private static string TitleCase(string[] tokens)
        => string.Join(' ', tokens.Select((token, index) =>
            index > 0 && TitleCaseSmallWords.Contains(token, StringComparer.OrdinalIgnoreCase)
                ? token.ToLowerInvariant()
                : char.ToUpperInvariant(token[0]) + token[1..]));

    private static readonly string[] TitleCaseSmallWords =
    [
        "and", "of", "the", "in", "on", "at", "to", "for", "from", "with"
    ];

    private static readonly string[] DisplayStopWords =
    [
        "and", "part", "chapter", "episode", "volume", "vol", "season",
        "series", "movie", "film", "the", "of", "collection"
    ];

    private static string[] CommonPrefix(string[] left, string[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var result = new List<string>(length);
        for (var i = 0; i < length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            result.Add(left[i]);
        }

        return result.ToArray();
    }

    private static bool IsFranchisePrefix(string[] common, string[] left, string[] right, PluginConfiguration config)
    {
        if (common.Length == 0)
        {
            return false;
        }

        var phrase = string.Join(' ', common);
        if (GenericPrefixes.Contains(phrase, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var minimumLength = common.Length >= config.MinFranchisePrefixTokens
            ? Math.Min(4, config.MinFranchisePrefixLength)
            : config.MinFranchisePrefixLength;

        if (phrase.Length < minimumLength)
        {
            return false;
        }

        if (common.Length >= Math.Max(1, config.MinFranchisePrefixTokens))
        {
            return true;
        }

        // A single shared word is only enough when it is the whole shorter title:
        // "Batman" / "Batman Returns" is a franchise; "Star Wars" / "Star Trek" is not.
        return left.Length == common.Length || right.Length == common.Length;
    }

    private static readonly string[] GenericPrefixes =
    [
        "the", "and", "part", "chapter", "episode", "volume", "series",
        "collection", "movie", "films", "best", "ultimate", "complete",
        "original", "official", "new", "full", "hd", "uhd", "remastered"
    ];

    private static string? GetYouTubeChannel(BaseItem item)
    {
        if (item.ProviderIds.TryGetValue(YouTubeMetadataService.YouTubeChannelProviderKey, out var channel)
            && !string.IsNullOrWhiteSpace(channel))
        {
            return channel.Trim();
        }

        return (item.Studios ?? [])
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)
                                 && !s.Equals("YouTube", StringComparison.OrdinalIgnoreCase))?
            .Trim();
    }

    private static string CleanDisplayTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        return BracketTextRegex().Replace(title, " ").Trim();
    }

    private static string SanitizeName(string name)
    {
        var chars = name.Replace('\t', ' ').ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (InvalidNameChars.Contains(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        return WhitespaceRegex().Replace(new string(chars), " ").Trim();
    }

    [GeneratedRegex(@"\s*[\[\(\{][^\]\)\}]*[\]\)\}]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex BracketTextRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
