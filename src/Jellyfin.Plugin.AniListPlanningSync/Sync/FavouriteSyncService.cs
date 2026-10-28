using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.AniListPlanningSync.AniList;
using Jellyfin.Plugin.AniListPlanningSync.AniSync;
using Jellyfin.Plugin.AniListPlanningSync.Configuration;
using Jellyfin.Plugin.AniListPlanningSync.Mapping;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListPlanningSync.Sync;

public class FavouriteSyncService
{
    private readonly AniListClient _aniList;
    private readonly AniSyncTokenReader _aniSync;
    private readonly AnimeMappingStore _mappingStore;
    private readonly LibraryMatcher _matcher;
    private readonly SyncStateStore _stateStore;
    private readonly IConfigurationAccessor _configuration;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<FavouriteSyncService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FavouriteSyncService(
        AniListClient aniList,
        AniSyncTokenReader aniSync,
        AnimeMappingStore mappingStore,
        LibraryMatcher matcher,
        SyncStateStore stateStore,
        IConfigurationAccessor configuration,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        ILogger<FavouriteSyncService> logger)
    {
        _aniList = aniList;
        _aniSync = aniSync;
        _mappingStore = mappingStore;
        _matcher = matcher;
        _stateStore = stateStore;
        _configuration = configuration;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    private enum SyncMode
    {
        Full,
        QuickCheck,
        CachedOnly
    }

    /// <summary>Fetches the full Planning list of every opted-in user and applies it.</summary>
    public Task SyncAllAsync(IProgress<double>? progress, CancellationToken cancellationToken)
        => RunForAllAsync(SyncMode.Full, progress, cancellationToken);

    /// <summary>Checks each opted-in user's list for changes with a single request, and syncs only changed lists.</summary>
    public Task QuickCheckAllAsync(IProgress<double>? progress, CancellationToken cancellationToken)
        => RunForAllAsync(SyncMode.QuickCheck, progress, cancellationToken);

    /// <summary>Re-applies the cached Planning lists to the library without contacting AniList.</summary>
    public Task RematchCachedAsync(CancellationToken cancellationToken)
        => RunForAllAsync(SyncMode.CachedOnly, null, cancellationToken);

    public async Task SyncUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SyncUserLockedAsync(userId, SyncMode.Full, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RunForAllAsync(SyncMode mode, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var users = _configuration.Configuration.Users.Where(u => u.IsEnabled).Select(u => u.UserId).ToList();
            for (var i = 0; i < users.Count; i++)
            {
                await SyncUserLockedAsync(users[i], mode, cancellationToken).ConfigureAwait(false);
                progress?.Report(100.0 * (i + 1) / users.Count);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SyncUserLockedAsync(Guid userId, SyncMode mode, CancellationToken cancellationToken)
    {
        var settings = _configuration.GetUser(userId);
        if (settings is null || !settings.IsEnabled)
        {
            return;
        }

        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            _logger.LogWarning("Jellyfin user {UserId} no longer exists, skipping", userId);
            return;
        }

        var state = _stateStore.Get(userId);
        var sourceKey = settings.Source == LoginSource.AniSync
            ? "AniSync"
            : "Username:" + settings.AniListUserName!.Trim().ToLowerInvariant();
        if (state.SourceKey != sourceKey)
        {
            state.SourceKey = sourceKey;
            state.PlanningIds.Clear();
            state.Marker = null;
            state.TokenHash = null;
            state.AniListUserId = null;
            if (mode == SyncMode.QuickCheck)
            {
                mode = SyncMode.Full;
            }
        }

        try
        {
            if (mode != SyncMode.CachedOnly)
            {
                var aniListUser = await ResolveAniListUserAsync(settings, state, cancellationToken).ConfigureAwait(false);
                if (aniListUser is null)
                {
                    return;
                }

                if (mode == SyncMode.QuickCheck)
                {
                    var marker = await _aniList.GetPlanningMarkerAsync(aniListUser, cancellationToken).ConfigureAwait(false);
                    if (marker == state.Marker)
                    {
                        _stateStore.Save();
                        return;
                    }

                    _logger.LogInformation("Planning list of {User} changed, syncing", user.Username);
                }

                var list = await _aniList.GetPlanningListAsync(aniListUser, cancellationToken).ConfigureAwait(false);
                state.PlanningIds = list.MediaIds.ToHashSet();
                state.Marker = list.Marker;
            }

            var mapping = _configuration.Configuration.UseMappingFallback
                ? await _mappingStore.GetIndexAsync(cancellationToken).ConfigureAwait(false)
                : null;
            var targets = _matcher.FindTargets(user, state.PlanningIds, mapping);
            var added = ApplyFavourites(user, targets, state, cancellationToken);

            _logger.LogInformation(
                "{User}: {Planning} planned anime, {Matched} in library, {Added} newly favourited",
                user.Username,
                state.PlanningIds.Count,
                targets.Count,
                added);
        }
        catch (AniListException ex)
        {
            _logger.LogWarning("Could not sync AniList Planning list for {User}: {Message}", user.Username, ex.Message);
        }
        finally
        {
            _stateStore.Save();
        }
    }

    private async Task<AniListUserRef?> ResolveAniListUserAsync(UserSettings settings, UserSyncState state, CancellationToken cancellationToken)
    {
        if (settings.Source == LoginSource.Username)
        {
            return new AniListUserRef(null, settings.AniListUserName!.Trim(), null);
        }

        var token = _aniSync.GetToken(settings.UserId);
        if (token is null)
        {
            _logger.LogWarning("No AniList login found in Ani-Sync for user {UserId}, skipping", settings.UserId);
            return null;
        }

        var tokenHash = HashToken(token);
        if (state.TokenHash != tokenHash || state.AniListUserId is null)
        {
            var viewer = await _aniList.GetViewerAsync(token, cancellationToken).ConfigureAwait(false);
            state.TokenHash = tokenHash;
            state.AniListUserId = viewer.Id;
        }

        return new AniListUserRef(state.AniListUserId, null, token);
    }

    /// <summary>
    /// Marks the given items as favourites, skipping items handled in an earlier run.
    /// This only ever sets IsFavorite to true.
    /// </summary>
    internal int ApplyFavourites(User user, IEnumerable<Guid> targets, UserSyncState state, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var itemId in targets)
        {
            if (state.AppliedItemIds.Contains(itemId))
            {
                continue;
            }

            var item = _libraryManager.GetItemById(itemId);
            var data = item is null ? null : _userDataManager.GetUserData(user, item);
            if (item is null || data is null)
            {
                continue;
            }

            if (!data.IsFavorite)
            {
                data.IsFavorite = true;
                _userDataManager.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, cancellationToken);
                _logger.LogInformation("Marked {Item} as favourite for {User}", item.Name, user.Username);
                added++;
            }

            state.AppliedItemIds.Add(itemId);
        }

        return added;
    }

    internal static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
