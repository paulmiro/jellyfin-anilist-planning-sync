using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListPlanningSync.Sync;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListPlanningSync;

/// <summary>
/// Matches newly added library items against the cached Planning lists. Never contacts AniList.
/// </summary>
public sealed class LibraryWatcher : IHostedService, IDisposable
{
    internal static readonly TimeSpan Debounce = TimeSpan.FromMinutes(2);

    private readonly ILibraryManager _libraryManager;
    private readonly FavouriteSyncService _sync;
    private readonly ILogger<LibraryWatcher> _logger;
    private readonly Timer _timer;

    public LibraryWatcher(ILibraryManager libraryManager, FavouriteSyncService sync, ILogger<LibraryWatcher> logger)
    {
        _libraryManager = libraryManager;
        _sync = sync;
        _logger = logger;
        _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer.Dispose();

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Series or Season or Movie)
        {
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private async void OnTimer(object? state)
    {
        try
        {
            await _sync.RematchCachedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Matching new library items against cached Planning lists failed");
        }
    }
}
