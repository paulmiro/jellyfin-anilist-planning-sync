using Jellyfin.Plugin.AniListPlanningSync.AniList;
using Jellyfin.Plugin.AniListPlanningSync.AniSync;
using Jellyfin.Plugin.AniListPlanningSync.Configuration;
using Jellyfin.Plugin.AniListPlanningSync.Mapping;
using Jellyfin.Plugin.AniListPlanningSync.Sync;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AniListPlanningSync;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IConfigurationAccessor, PluginConfigurationAccessor>();
        serviceCollection.AddSingleton<AniListClient>();
        serviceCollection.AddSingleton<AniSyncTokenReader>();
        serviceCollection.AddSingleton<AnimeMappingStore>();
        serviceCollection.AddSingleton<LibraryMatcher>();
        serviceCollection.AddSingleton<SyncStateStore>();
        serviceCollection.AddSingleton<FavouriteSyncService>();
        serviceCollection.AddSingleton<UserSettingsService>();
        serviceCollection.AddHostedService<LibraryWatcher>();
    }
}
