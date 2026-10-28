using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListPlanningSync.Configuration;
using Jellyfin.Plugin.AniListPlanningSync.Mapping;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.AniListPlanningSync.Tasks;

public class RefreshMappingTask : IScheduledTask
{
    private readonly AnimeMappingStore _store;
    private readonly IConfigurationAccessor _configuration;

    public RefreshMappingTask(AnimeMappingStore store, IConfigurationAccessor configuration)
    {
        _store = store;
        _configuration = configuration;
    }

    public string Name => "Refresh anime id mapping";

    public string Key => "AniListPlanningSyncRefreshMapping";

    public string Description => "Downloads the anime id mapping file (Fribb/anime-lists) used to match items that have no AniList id.";

    public string Category => "Planning Favourites for AniList";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _configuration.Configuration.UseMappingFallback
            ? _store.RefreshAsync(cancellationToken)
            : Task.CompletedTask;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(1).Ticks
        };
    }
}
