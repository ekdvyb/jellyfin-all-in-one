using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AllInOne.Configuration;

/// <summary>
/// Thumbnail size preference for YouTube artwork.
/// </summary>
public enum ThumbnailPreference
{
    /// <summary>1280x720 when available, falls back to hqdefault.</summary>
    MaxResolution,

    /// <summary>640x480, falls back to hqdefault.</summary>
    StandardDefinition,

    /// <summary>480x360, always available.</summary>
    HighQuality
}

/// <summary>
/// Plugin settings.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        CollectionPrefix = "⭐ ";
        PreferredThumbnail = ThumbnailPreference.MaxResolution;
    }

    // ---------------- Collections ----------------

    /// <summary>Gets or sets a value indicating whether automatic collections are enabled.</summary>
    public bool EnableAutoCollections { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to create an "All Movies" collection.</summary>
    public bool CreateAllMoviesCollection { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to create an "All TV Shows" collection.</summary>
    public bool CreateAllShowsCollection { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to create per-genre collections.</summary>
    public bool CreateGenreCollections { get; set; }

    /// <summary>Gets or sets a value indicating whether to create per-decade collections.</summary>
    public bool CreateDecadeCollections { get; set; }

    /// <summary>Gets or sets a value indicating whether to sync after library changes.</summary>
    public bool RunOnLibraryChange { get; set; } = true;

    /// <summary>Gets or sets the minimum number of items needed to create a collection.</summary>
    public int MinItemsPerCollection { get; set; } = 2;

    /// <summary>Gets or sets the prefix prepended to every managed collection name.</summary>
    public string CollectionPrefix { get; set; } = "⭐ ";

    // ---------------- YouTube metadata ----------------

    /// <summary>Gets or sets a value indicating whether YouTube metadata fetching is enabled.</summary>
    public bool EnableYouTubeMetadata { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether new videos are processed automatically.</summary>
    public bool ProcessOnLibraryChange { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the item name is replaced with the YouTube title.</summary>
    public bool RenameVideoFromYouTube { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the description is written to the item overview.</summary>
    public bool DownloadDescription { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether thumbnails are attached as images.</summary>
    public bool FetchThumbnails { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether already tagged videos are refreshed again.</summary>
    public bool OverwriteExistingMetadata { get; set; }

    /// <summary>Gets or sets the thumbnail size preference.</summary>
    public ThumbnailPreference PreferredThumbnail { get; set; } = ThumbnailPreference.MaxResolution;

    /// <summary>Gets or sets the minimum delay between YouTube requests in milliseconds.</summary>
    public int RequestDelayMs { get; set; } = 700;
}
