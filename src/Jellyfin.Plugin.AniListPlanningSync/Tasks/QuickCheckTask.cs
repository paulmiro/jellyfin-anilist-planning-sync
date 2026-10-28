using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListPlanningSync.Configuration;
using Jellyfin.Plugin.AniListPlanningSync.Sync;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.AniListPlanningSync.Tasks;

/// <summary>
/// AniList offers no webhooks, so changes are detected by polling the most recently updated Planning entry.
/// </summary>
public class QuickCheckTask : IScheduledTask
{
    private readonly FavouriteSyncService _sync;
    private readonly IConfigurationAccessor _configuration;

    public QuickCheckTask(FavouriteSyncService sync, IConfigurationAccessor configuration)
    {
        _sync = sync;
        _configuration = configuration;
    }

    public string Name => "Check AniList Planning lists for changes";

    public string Key => "AniListPlanningSyncQuickCheck";

    public string Description => "Uses one request per user to detect new Planning entries and syncs only lists that changed.";

    public string Category => "Planning Favourites for AniList";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _configuration.Configuration.QuickCheckEnabled
            ? _sync.QuickCheckAllAsync(progress, cancellationToken)
            : Task.CompletedTask;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromMinutes(10).Ticks
        };
    }
}
