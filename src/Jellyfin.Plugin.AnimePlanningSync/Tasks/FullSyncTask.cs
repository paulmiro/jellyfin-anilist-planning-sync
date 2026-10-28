using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnimePlanningSync.Sync;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.AnimePlanningSync.Tasks;

public class FullSyncTask : IScheduledTask
{
    private readonly FavouriteSyncService _sync;

    public FullSyncTask(FavouriteSyncService sync)
    {
        _sync = sync;
    }

    public string Name => "Full sync of AniList Planning lists";

    public string Key => "AnimePlanningSyncFull";

    public string Description => "Fetches the complete AniList Planning list of every opted-in user and marks matching library items as favourites.";

    public string Category => "Anime Planning Sync";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _sync.SyncAllAsync(progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(1).Ticks
        };
    }
}
