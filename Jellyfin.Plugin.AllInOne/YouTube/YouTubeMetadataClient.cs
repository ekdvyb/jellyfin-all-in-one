using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AllInOne.YouTube;

/// <summary>
/// Metadata returned for a YouTube video.
/// </summary>
/// <param name="VideoId">The YouTube video id.</param>
/// <param name="Title">Video title.</param>
/// <param name="Author">Channel name.</param>
/// <param name="Description">Video description.</param>
/// <param name="ThumbnailUrl">Best available thumbnail URL.</param>
public sealed record YouTubeMetadata(
    string VideoId,
    string? Title,
    string? Author,
    string? Description,
    string? ThumbnailUrl);

/// <summary>
/// Fetches YouTube metadata without requiring an API key (oEmbed + watch page).
/// </summary>
public sealed partial class YouTubeMetadataClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YouTubeMetadataClient> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="YouTubeMetadataClient"/> class.
    /// </summary>
    public YouTubeMetadataClient(IHttpClientFactory httpClientFactory, ILogger<YouTubeMetadataClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets metadata for a video id, using a short-lived cache.
    /// </summary>
    public async Task<YouTubeMetadata?> GetAsync(
        string videoId,
        int requestDelayMs,
        string preferredThumbnail,
        bool includeDescription,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(videoId, out var cached)
            && DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromHours(12))
        {
            return cached.Metadata;
        }

        var metadata = await FetchOEmbedAsync(videoId, requestDelayMs, cancellationToken).ConfigureAwait(false);

        // oEmbed has no description, so only hit the heavier watch page when one is wanted.
        if (includeDescription && (metadata is null || string.IsNullOrWhiteSpace(metadata.Description)))
        {
            var fromPage = await FetchWatchPageAsync(videoId, requestDelayMs, cancellationToken)
                .ConfigureAwait(false);
            if (fromPage is not null)
            {
                metadata = metadata is null
                    ? fromPage
                    : metadata with
                    {
                        Title = string.IsNullOrWhiteSpace(metadata.Title) ? fromPage.Title : metadata.Title,
                        Author = metadata.Author ?? fromPage.Author,
                        Description = fromPage.Description
                    };
            }
        }

        if (metadata is not null)
        {
            var thumbnail = await ResolveThumbnailAsync(videoId, preferredThumbnail, cancellationToken)
                .ConfigureAwait(false);
            metadata = metadata with { ThumbnailUrl = thumbnail };
            _cache[videoId] = new CacheEntry(metadata, DateTime.UtcNow);
        }

        return metadata;
    }

    private async Task<YouTubeMetadata?> FetchOEmbedAsync(
        string videoId,
        int requestDelayMs,
        CancellationToken cancellationToken)
    {
        var watchUrl = "https://www.youtube.com/watch?v=" + videoId;
        var url = "https://www.youtube.com/oembed?url="
                  + Uri.EscapeDataString(watchUrl)
                  + "&format=json";

        try
        {
            await ThrottleAsync(requestDelayMs, cancellationToken).ConfigureAwait(false);
            using var client = CreateClient();
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            string? title = null;
            string? author = null;
            if (root.TryGetProperty("title", out var titleElement))
            {
                title = titleElement.GetString();
            }

            if (root.TryGetProperty("author_name", out var authorElement))
            {
                author = authorElement.GetString();
            }

            if (string.IsNullOrWhiteSpace(title) && author is null)
            {
                return null;
            }

            _logger.LogDebug("oEmbed succeeded for YouTube video {VideoId}.", videoId);
            return new YouTubeMetadata(videoId, title, author, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "oEmbed failed for YouTube video {VideoId}; trying the watch page.", videoId);
            return null;
        }
    }

    private async Task<YouTubeMetadata?> FetchWatchPageAsync(
        string videoId,
        int requestDelayMs,
        CancellationToken cancellationToken)
    {
        try
        {
            await ThrottleAsync(requestDelayMs, cancellationToken).ConfigureAwait(false);
            using var client = CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://www.youtube.com/watch?v={videoId}&hl=en");
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
                + "Chrome/124.0.0.0 Safari/537.36");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            string? title = null;
            string? description = null;
            string? author = null;

            foreach (Match tag in MetaTagRegex().Matches(html))
            {
                var attributes = ParseAttributes(tag.Value);
                if (!attributes.TryGetValue("property", out var key)
                    && !attributes.TryGetValue("name", out key)
                    && !attributes.TryGetValue("itemprop", out key))
                {
                    continue;
                }

                if (!attributes.TryGetValue("content", out var content))
                {
                    continue;
                }

                switch (key.ToLowerInvariant())
                {
                    case "og:title":
                    case "title":
                        title ??= content;
                        break;
                    case "og:description":
                    case "description":
                        description ??= content;
                        break;
                    case "author":
                        author ??= content;
                        break;
                }
            }

            var shortDescription = ShortDescriptionRegex().Match(html);
            if (shortDescription.Success)
            {
                description ??= UnescapeJsonString(shortDescription.Groups["v"].Value);
            }

            var videoDetailsTitle = VideoDetailsTitleRegex().Match(html);
            if (videoDetailsTitle.Success)
            {
                title ??= UnescapeJsonString(videoDetailsTitle.Groups["v"].Value);
            }

            var authorMatch = AuthorRegex().Match(html);
            if (authorMatch.Success)
            {
                author ??= UnescapeJsonString(authorMatch.Groups["v"].Value);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            _logger.LogDebug("Watch page succeeded for YouTube video {VideoId}.", videoId);
            return new YouTubeMetadata(videoId, title, author, description, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the watch page for YouTube video {VideoId}.", videoId);
            return null;
        }
    }

    private async Task<string?> ResolveThumbnailAsync(
        string videoId,
        string preferredThumbnail,
        CancellationToken cancellationToken)
    {
        var candidates = new[] { preferredThumbnail, "sddefault", "hqdefault", "mqdefault" }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var url = $"https://i.ytimg.com/vi/{videoId}/{candidate}.jpg";
            try
            {
                await ThrottleAsync(0, cancellationToken).ConfigureAwait(false);
                using var client = CreateClient();
                using var response = await client.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                // YouTube serves a tiny 120x90 placeholder when a requested size is unavailable.
                if (response.Content.Headers.ContentLength is > 5000)
                {
                    return url;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Thumbnail check failed for {Url}.", url);
            }
        }

        // hqdefault is effectively always present.
        return $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(25);
        return client;
    }

    private async Task ThrottleAsync(int delayMs, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var remaining = TimeSpan.FromMilliseconds(Math.Max(0, delayMs))
                            - (DateTime.UtcNow - _lastRequestUtc);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static Dictionary<string, string> ParseAttributes(string tag)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(tag))
        {
            var value = match.Groups[2].Success
                ? match.Groups[2].Value
                : match.Groups[3].Success
                    ? match.Groups[3].Value
                    : match.Groups[4].Value;
            result[match.Groups[1].Value] = WebUtility.HtmlDecode(value);
        }

        return result;
    }

    private static string? UnescapeJsonString(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + raw + "\"");
        }
        catch (JsonException)
        {
            return WebUtility.HtmlDecode(raw);
        }
    }

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex(@"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"""shortDescription"":""(?<v>(?:\\.|[^""\\])*)""", RegexOptions.CultureInvariant)]
    private static partial Regex ShortDescriptionRegex();

    [GeneratedRegex(@"""videoDetails""\s*:\s*\{[^}]*?""title"":""(?<v>(?:\\.|[^""\\])*)""", RegexOptions.CultureInvariant)]
    private static partial Regex VideoDetailsTitleRegex();

    [GeneratedRegex(@"""author"":""(?<v>(?:\\.|[^""\\])*)""", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorRegex();

    private sealed record CacheEntry(YouTubeMetadata Metadata, DateTime FetchedAt);
}
