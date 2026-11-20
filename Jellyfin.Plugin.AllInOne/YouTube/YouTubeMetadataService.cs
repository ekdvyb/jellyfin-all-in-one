using System.Text;
using System.Threading.Channels;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AllInOne.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AllInOne.YouTube;

/// <summary>
/// Applies YouTube metadata (title, description, thumbnails) to video items.
/// </summary>
public sealed class YouTubeMetadataService : IDisposable
{
    /// <summary>Provider id key holding the YouTube channel name.</summary>
    public const string YouTubeChannelProviderKey = "YouTubeChannel";

    private const string ProcessedTag = "aio-youtube";
    private const string NotFoundPrefix = "aio-yt-notfound-";
    private const string FailedPrefix = "aio-yt-failed-";
    private const string RetryAfterDays = "3";

    private readonly YouTubeMetadataClient _client;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<YouTubeMetadataService> _logger;
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="YouTubeMetadataService"/> class.
    /// </summary>
    public YouTubeMetadataService(
        YouTubeMetadataClient client,
        ILibraryManager libraryManager,
        ILogger<YouTubeMetadataService> logger)
    {
        _client = client;
        _libraryManager = libraryManager;
        _logger = logger;

        _libraryManager.ItemAdded += OnItemAdded;
        _worker = Task.Run(ProcessQueueAsync);
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs args)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableYouTubeMetadata != true || !config.ProcessOnLibraryChange)
        {
            return;
        }

        if (args.Item is Video && args.Item is not Movie)
        {
            _queue.Writer.TryWrite(args.Item.Id);
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var itemId in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            try
            {
                // Give Jellyfin a moment to finish its own metadata/provider work for the new item.
                await Task.Delay(TimeSpan.FromSeconds(2), _shutdown.Token).ConfigureAwait(false);
                await ProcessAsync(itemId, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process YouTube metadata for item {ItemId}.", itemId);
            }
        }
    }

    /// <summary>
    /// Processes every video in the library that has not been handled yet.
    /// </summary>
    public async Task SyncAllAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.EnableYouTubeMetadata)
        {
            return;
        }

        var videos = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Video, BaseItemKind.MusicVideo],
            Recursive = true,
            IsVirtualItem = false
        })
        .OfType<Video>()
        .Where(i => i is not Movie)
        .Where(ShouldProcess)
        .ToList();

        _logger.LogInformation("Checking YouTube metadata for {Count} videos.", videos.Count);

        for (var i = 0; i < videos.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessAsync(videos[i].Id, cancellationToken).ConfigureAwait(false);
            progress?.Report(100d * (i + 1) / videos.Count);
        }
    }

    private bool ShouldProcess(Video video)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (config.OverwriteExistingMetadata)
        {
            return true;
        }

        if (HasTag(video, ProcessedTag))
        {
            return false;
        }

        return !HasRecentFailureTag(video);
    }

    /// <summary>
    /// Processes a single library item.
    /// </summary>
    public async Task ProcessAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.EnableYouTubeMetadata)
        {
            return;
        }

        var item = _libraryManager.GetItemById<Video>(itemId);
        if (item is null || string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        if (!config.OverwriteExistingMetadata && HasTag(item, ProcessedTag))
        {
            return;
        }

        var videoId = FindVideoId(item);
        if (videoId is null)
        {
            MarkFailure(item, NotFoundPrefix);
            await SaveAsync(item, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("No YouTube id found for '{Name}'.", item.Name);
            return;
        }

        var preferred = config.PreferredThumbnail switch
        {
            ThumbnailPreference.StandardDefinition => "sddefault",
            ThumbnailPreference.HighQuality => "hqdefault",
            _ => "maxresdefault"
        };

        var metadata = await _client.GetAsync(
            videoId,
            config.RequestDelayMs,
            preferred,
            config.DownloadDescription,
            cancellationToken).ConfigureAwait(false);

        if (metadata is null)
        {
            MarkFailure(item, FailedPrefix);
            await SaveAsync(item, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("No metadata could be fetched for YouTube video {VideoId}.", videoId);
            return;
        }

        if (config.RenameVideoFromYouTube
            && !string.IsNullOrWhiteSpace(metadata.Title)
            && !string.Equals(item.Name, metadata.Title, StringComparison.Ordinal))
        {
            item.Name = metadata.Title.Trim();
        }

        if (config.DownloadDescription
            && !string.IsNullOrWhiteSpace(metadata.Description)
            && !string.Equals(item.Overview, metadata.Description, StringComparison.Ordinal))
        {
            item.Overview = metadata.Description.Trim();
        }

        item.ProviderIds["YouTube"] = videoId;

        // The channel is what makes YouTube videos groupable like a studio/franchise.
        if (!string.IsNullOrWhiteSpace(metadata.Author))
        {
            var channel = metadata.Author.Trim();
            item.ProviderIds[YouTubeChannelProviderKey] = channel;
            if (config.StoreChannelAsStudio)
            {
                item.Studios = [channel];
            }
        }

        if (config.FetchThumbnails)
        {
            ApplyImages(item, videoId, metadata.ThumbnailUrl);
        }

        ClearFailureTags(item);
        AddTag(item, ProcessedTag);
        await SaveAsync(item, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Applied YouTube metadata to '{Name}' ({VideoId}).",
            item.Name,
            videoId);
    }

    private async Task SaveAsync(Video item, CancellationToken cancellationToken)
    {
        await _libraryManager.UpdateItemAsync(
            item,
            item.GetParent(),
            ItemUpdateType.MetadataDownload,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds a YouTube video id in an item's provider ids, filename, name or sidecar files.
    /// </summary>
    public static string? FindVideoId(BaseItem item)
    {
        if (item.ProviderIds.TryGetValue("YouTube", out var providerId)
            && YouTubeIdParser.IsValidId(providerId))
        {
            return providerId;
        }

        var fileName = Path.GetFileName(item.Path);
        var id = YouTubeIdParser.Parse(item.Name)
                 ?? YouTubeIdParser.Parse(fileName)
                 ?? YouTubeIdParser.Parse(ReadSidecar(item.Path));

        return YouTubeIdParser.IsValidId(id) ? id : null;
    }

    private static string? ReadSidecar(string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var extension in new[] { ".youtube", ".nfo" })
        {
            string sidecar;
            try
            {
                sidecar = Path.ChangeExtension(videoPath, extension);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!File.Exists(sidecar))
            {
                continue;
            }

            try
            {
                using var reader = new StreamReader(sidecar, Encoding.UTF8, true);
                var buffer = new char[8192];
                var read = reader.Read(buffer, 0, buffer.Length);
                builder.Append(buffer, 0, read);
                builder.AppendLine();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static void ApplyImages(Video item, string videoId, string? preferredUrl)
    {
        var primaryUrl = !string.IsNullOrWhiteSpace(preferredUrl)
            ? preferredUrl!
            : $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";

        var backdropUrl = primaryUrl.Contains("maxresdefault", StringComparison.OrdinalIgnoreCase)
                          || primaryUrl.Contains("mqdefault", StringComparison.OrdinalIgnoreCase)
            ? primaryUrl
            : $"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg";

        SetImage(item, ImageType.Primary, primaryUrl);
        SetImage(item, ImageType.Backdrop, backdropUrl);
    }

    private static void SetImage(BaseItem item, ImageType type, string url)
    {
        var existing = item.GetImages(type).FirstOrDefault();
        if (existing is not null
            && string.Equals(existing.Path, url, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var old in item.GetImages(type).ToList())
        {
            item.RemoveImage(old);
        }

        item.SetImage(new ItemImageInfo
        {
            Path = url,
            Type = type
        }, 0);
    }

    private static bool HasTag(BaseItem item, string tag)
        => item.Tags is not null
           && item.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

    private static void AddTag(BaseItem item, string tag)
    {
        var tags = item.Tags is null
            ? new List<string>()
            : item.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        if (tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        tags.Add(tag);
        item.Tags = tags.ToArray();
    }

    private static bool HasRecentFailureTag(BaseItem item)
    {
        if (item.Tags is null)
        {
            return false;
        }

        foreach (var tag in item.Tags)
        {
            if (tag is null || (!tag.StartsWith(NotFoundPrefix, StringComparison.OrdinalIgnoreCase)
                                && !tag.StartsWith(FailedPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var dateText = tag[(tag.LastIndexOf('-') + 1)..];
            if (DateTime.TryParseExact(
                    dateText,
                    "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var date)
                && DateTime.UtcNow - date < TimeSpan.FromDays(int.Parse(RetryAfterDays)))
            {
                return true;
            }
        }

        return false;
    }

    private static void MarkFailure(BaseItem item, string prefix)
    {
        ClearFailureTags(item);
        AddTag(item, prefix + DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void ClearFailureTags(BaseItem item)
    {
        if (item.Tags is null)
        {
            return;
        }

        item.Tags = item.Tags
            .Where(t => t is not null
                        && !t.StartsWith(NotFoundPrefix, StringComparison.OrdinalIgnoreCase)
                        && !t.StartsWith(FailedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libraryManager.ItemAdded -= OnItemAdded;
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
        _shutdown.Dispose();
    }
}
