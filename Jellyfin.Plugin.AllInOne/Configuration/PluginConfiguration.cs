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
    /// Incremented when the collection strategy changed from broad "All Movies" lists
    /// to the smart grouping rules.
    /// </summary>
    public int SchemaVersion { get; set; } = 2;

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

    /// <summary>Gets or sets a value indicating whether to detect movie franchises/trilogies from titles.</summary>
    public bool GroupByFranchise { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to group releases by studio.</summary>
    public bool GroupByStudio { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to group items that share a title.</summary>
    public bool GroupByTitle { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to group TV episodes by series.</summary>
    public bool GroupBySeries { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to group by genre.</summary>
    public bool GroupByGenre { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to create broad type collections.</summary>
    public bool GroupByType { get; set; }

    /// <summary>Gets or sets a value indicating whether to group YouTube videos by channel.</summary>
    public bool GroupByYouTubeChannel { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to add decade collections.</summary>
    public bool GroupByDecade { get; set; }

    /// <summary>Gets or sets a value indicating whether to sync after library changes.</summary>
    public bool RunOnLibraryChange { get; set; } = true;

    /// <summary>Gets or sets the minimum number of items needed to create a collection.</summary>
    public int MinItemsPerCollection { get; set; } = 2;

    /// <summary>Gets or sets the prefix prepended to every managed collection name.</summary>
    public string CollectionPrefix { get; set; } = "⭐ ";

    /// <summary>
    /// Gets or sets how many shared title words are enough to call two items a franchise
    /// when neither title is simply the shorter one.
    /// </summary>
    public int MinFranchisePrefixTokens { get; set; } = 2;

    /// <summary>
    /// Gets or sets the minimum length of a shared franchise prefix.
    /// </summary>
    public int MinFranchisePrefixLength { get; set; } = 5;

    // ---------------- Legacy settings (kept so old config files still load) ----------------

    /// <summary>Legacy setting, ignored by the smart grouping engine.</summary>
    public bool CreateAllMoviesCollection { get; set; }

    /// <summary>Legacy setting, ignored by the smart grouping engine.</summary>
    public bool CreateAllShowsCollection { get; set; }

    /// <summary>Legacy setting, ignored by the smart grouping engine.</summary>
    public bool CreateGenreCollections { get; set; }

    /// <summary>Legacy setting, ignored by the smart grouping engine.</summary>
    public bool CreateDecadeCollections { get; set; }

    // ---------------- YouTube metadata ----------------

    /// <summary>Gets or sets a value indicating whether YouTube metadata fetching is enabled.</summary>
    public bool EnableYouTubeMetadata { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether new videos are processed automatically.</summary>
    public bool ProcessOnLibraryChange { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the item name is replaced with the YouTube title.</summary>
    public bool RenameVideoFromYouTube { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the description is written to the item overview.</summary>
    public bool DownloadDescription { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the channel is stored as the video studio.</summary>
    public bool StoreChannelAsStudio { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether thumbnails are attached as images.</summary>
    public bool FetchThumbnails { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether already tagged videos are refreshed again.</summary>
    public bool OverwriteExistingMetadata { get; set; }

    /// <summary>Gets or sets the thumbnail size preference.</summary>
    public ThumbnailPreference PreferredThumbnail { get; set; } = ThumbnailPreference.MaxResolution;

    /// <summary>Gets or sets the minimum delay between YouTube requests in milliseconds.</summary>
    public int RequestDelayMs { get; set; } = 700;

    /// <summary>
    /// Disables the old broad collections when upgrading from a version 1 configuration.
    /// </summary>
    public void MigrateIfNeeded()
    {
        if (SchemaVersion >= 2)
        {
            return;
        }

        SchemaVersion = 2;
        CreateAllMoviesCollection = false;
        CreateAllShowsCollection = false;
        CreateGenreCollections = false;
        CreateDecadeCollections = false;
    }
}
