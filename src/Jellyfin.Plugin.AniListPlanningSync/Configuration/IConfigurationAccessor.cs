using System;
using System.Linq;

namespace Jellyfin.Plugin.AniListPlanningSync.Configuration;

public interface IConfigurationAccessor
{
    PluginConfiguration Configuration { get; }

    void Save();
}

public static class ConfigurationAccessorExtensions
{
    public static UserSettings? GetUser(this IConfigurationAccessor accessor, Guid userId)
        => accessor.Configuration.Users.FirstOrDefault(u => u.UserId == userId);
}

public class PluginConfigurationAccessor : IConfigurationAccessor
{
    public PluginConfiguration Configuration => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public void Save() => Plugin.Instance?.SaveConfiguration();
}
