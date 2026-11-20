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
    public string Name => "All-In-One: Sync collections & YouTube metadata";

    /// <inheritdoc />
    public string Key => "JellyfinAllInOneSync";

    /// <inheritdoc />
    public string Description =>
        "Rebuilds the automatic movie/TV collections and re-checks videos for YouTube titles and thumbnails.";

    /// <inheritdoc />
    public string Category => "All-In-One";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        progress.Report(0);

        if (config.EnableAutoCollections)
        {
            await _collectionSync.SyncAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        if (config.EnableYouTubeMetadata)
        {
            await _youtubeMetadata.SyncAllAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
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
