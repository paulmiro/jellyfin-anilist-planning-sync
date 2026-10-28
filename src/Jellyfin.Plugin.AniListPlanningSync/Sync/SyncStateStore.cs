using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListPlanningSync.Sync;

public class UserSyncState
{
    /// <summary>
    /// Items this plugin has already favourited (or found favourited). They are never touched again,
    /// so a favourite the user removes manually stays removed.
    /// </summary>
    public HashSet<Guid> AppliedItemIds { get; set; } = [];

    /// <summary>Identifies which AniList list the cached data below belongs to.</summary>
    public string? SourceKey { get; set; }

    public HashSet<int> PlanningIds { get; set; } = [];

    public string? Marker { get; set; }

    public string? TokenHash { get; set; }

    public int? AniListUserId { get; set; }
}

public class SyncState
{
    public Dictionary<Guid, UserSyncState> Users { get; set; } = [];
}

/// <summary>
/// Persists <see cref="SyncState"/> as JSON. Callers must serialize access (FavouriteSyncService does).
/// </summary>
public class SyncStateStore
{
    private readonly string _path;
    private readonly ILogger<SyncStateStore> _logger;
    private SyncState? _state;

    public SyncStateStore(ILogger<SyncStateStore> logger)
        : this(Path.Combine(Plugin.Instance?.DataFolderPath ?? throw new InvalidOperationException("Plugin not initialized"), "state.json"), logger)
    {
    }

    internal SyncStateStore(string path, ILogger<SyncStateStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public UserSyncState Get(Guid userId)
    {
        var state = Load();
        if (!state.Users.TryGetValue(userId, out var user))
        {
            state.Users[userId] = user = new UserSyncState();
        }

        return user;
    }

    public void Save()
    {
        var state = Load();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state));
        File.Move(tempPath, _path, true);
    }

    private SyncState Load()
    {
        if (_state is not null)
        {
            return _state;
        }

        try
        {
            if (File.Exists(_path))
            {
                _state = JsonSerializer.Deserialize<SyncState>(File.ReadAllText(_path));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Without the applied list, manually removed favourites could be re-added, so refuse to start fresh silently.
            _logger.LogError(ex, "Could not read sync state from {Path}", _path);
            throw;
        }

        _state ??= new SyncState();
        return _state;
    }
}
