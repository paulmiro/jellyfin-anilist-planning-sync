using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AnimePlanningSync.Configuration;

public enum LoginSource
{
    Off,
    AniSync,
    Username
}

public class UserSettings
{
    public Guid UserId { get; set; }

    public LoginSource Source { get; set; } = LoginSource.Off;

    public string? AniListUserName { get; set; }

    public bool IsEnabled => Source switch
    {
        LoginSource.AniSync => true,
        LoginSource.Username => !string.IsNullOrWhiteSpace(AniListUserName),
        _ => false
    };
}

public class PluginConfiguration : BasePluginConfiguration
{
    // Kept below AniList's currently degraded limit of 30 requests per minute.
    public int MaxRequestsPerMinute { get; set; } = 20;

    public bool QuickCheckEnabled { get; set; } = true;

    public bool UseMappingFallback { get; set; } = true;

    public List<UserSettings> Users { get; set; } = [];
}
