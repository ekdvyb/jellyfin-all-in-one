using System.Globalization;
using Jellyfin.Plugin.AllInOne.Collections;
using Jellyfin.Plugin.AllInOne.Configuration;
using Jellyfin.Plugin.AllInOne.YouTube;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AllInOne;

/// <summary>
/// Main entry point for the All-In-One plugin.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        CollectionSyncService collectionSyncService,
        YouTubeMetadataService youtubeMetadataService)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        Configuration.MigrateIfNeeded();
        SaveConfiguration();

        // Both services subscribe to library events in their constructors.
        // Resolving them here guarantees the subscriptions exist as soon as the plugin loads.
        _ = collectionSyncService;
        _ = youtubeMetadataService;
    }

    /// <inheritdoc />
    public override string Name => "Jellyfin All-In-One";

    /// <inheritdoc />
    public override string Description =>
        "Builds smart collections by franchise, studio, title, series and genre, and enriches " +
        "YouTube videos with titles, channels, descriptions and thumbnails.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginInfo.Guid);

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "AllInOne",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }
}

/// <summary>
/// Static identifiers shared between the plugin and its manifest.
/// </summary>
public static class PluginInfo
{
    /// <summary>
    /// The plugin GUID.
    /// </summary>
    public const string Guid = "bb1a6c35-3d1b-48af-8d0a-3c8d0ff039cb";
}
