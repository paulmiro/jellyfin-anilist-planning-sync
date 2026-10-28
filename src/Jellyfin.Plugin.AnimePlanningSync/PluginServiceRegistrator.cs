using Jellyfin.Plugin.AnimePlanningSync.AniList;
using Jellyfin.Plugin.AnimePlanningSync.AniSync;
using Jellyfin.Plugin.AnimePlanningSync.Configuration;
using Jellyfin.Plugin.AnimePlanningSync.Mapping;
using Jellyfin.Plugin.AnimePlanningSync.Sync;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AnimePlanningSync;

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
