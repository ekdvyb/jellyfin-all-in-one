using Jellyfin.Plugin.AllInOne.Collections;
using Jellyfin.Plugin.AllInOne.Configuration;
using Jellyfin.Plugin.AllInOne.YouTube;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AllInOne.Tasks;

/// <summary>
/// Scheduled task that rebuilds collections and refreshes YouTube metadata.
/// </summary>
public sealed class AllInOneScheduledTask : IScheduledTask
{
    private readonly CollectionSyncService _collectionSync;
    private readonly YouTubeMetadataService _youtubeMetadata;
    private readonly ILogger<AllInOneScheduledTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AllInOneScheduledTask"/> class.
    /// </summary>
    public AllInOneScheduledTask(
        CollectionSyncService collectionSync,
        YouTubeMetadataService youtubeMetadata,
        ILogger<AllInOneScheduledTask> logger)
    {
        _collectionSync = collectionSync;
        _youtubeMetadata = youtubeMetadata;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "All-In-One: Sync smart collections & YouTube metadata";

    /// <inheritdoc />
    public string Key => "JellyfinAllInOneSync";

    /// <inheritdoc />
    public string Description =>
        "Refreshes YouTube titles, channels and thumbnails first, then rebuilds smart collections " +
        "by franchise, studio, title, series, genre and channel.";

    /// <inheritdoc />
    public string Category => "All-In-One";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var reporter = progress;
        reporter.Report(0);

        // Fetch channel metadata first so the collection pass can group by it.
        if (config.EnableYouTubeMetadata)
        {
            IProgress<double>? youtubeProgress = reporter is null
                ? null
                : new Progress<double>(p => reporter.Report(p * 0.6));
            await _youtubeMetadata.SyncAllAsync(youtubeProgress, cancellationToken).ConfigureAwait(false);
        }

        if (config.EnableAutoCollections)
        {
            IProgress<double>? collectionProgress = reporter is null
                ? null
                : new Progress<double>(p => reporter.Report(60 + (p * 0.4)));
            await _collectionSync.SyncAsync(collectionProgress, cancellationToken).ConfigureAwait(false);
        }

        reporter?.Report(100);
        _logger.LogInformation("All-In-One scheduled task finished.");
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger
            }
        ];
    }
}
