using Jellyfin.Plugin.AllInOne.Collections;
using Jellyfin.Plugin.AllInOne.Tasks;
using Jellyfin.Plugin.AllInOne.YouTube;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AllInOne;

/// <summary>
/// Registers the plugin services with Jellyfin's dependency injection container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<CollectionSyncService>();
        services.AddSingleton<YouTubeMetadataClient>();
        services.AddSingleton<YouTubeMetadataService>();
        services.AddSingleton<IScheduledTask, AllInOneScheduledTask>();
    }
}
