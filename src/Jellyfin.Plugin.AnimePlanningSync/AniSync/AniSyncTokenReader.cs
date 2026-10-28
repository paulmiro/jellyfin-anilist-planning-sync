using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AnimePlanningSync.AniSync;

/// <summary>
/// Reads the AniList access tokens stored by the Ani-Sync plugin (github.com/vosmiic/jellyfin-ani-sync).
/// The file is only ever read, never written.
/// </summary>
public class AniSyncTokenReader
{
    internal const string ConfigFileName = "jellyfin-ani-sync.xml";

    private readonly string _configPath;
    private readonly ILogger<AniSyncTokenReader> _logger;

    public AniSyncTokenReader(IApplicationPaths applicationPaths, ILogger<AniSyncTokenReader> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, ConfigFileName), logger)
    {
    }

    internal AniSyncTokenReader(string configPath, ILogger<AniSyncTokenReader> logger)
    {
        _configPath = configPath;
        _logger = logger;
    }

    public string? GetToken(Guid jellyfinUserId)
        => ReadTokens().TryGetValue(jellyfinUserId, out var token) ? token : null;

    public IReadOnlyDictionary<Guid, string> ReadTokens()
    {
        var tokens = new Dictionary<Guid, string>();
        if (!File.Exists(_configPath))
        {
            _logger.LogDebug("Ani-Sync configuration not found at {Path}", _configPath);
            return tokens;
        }

        XDocument doc;
        try
        {
            using var stream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            doc = XDocument.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or XmlException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read Ani-Sync configuration at {Path}", _configPath);
            return tokens;
        }

        // Structure: PluginConfiguration/UserConfig/UserConfig{UserId, UserApiAuth/UserApiAuth{Name, AccessToken}}
        var userConfigs = Children(doc.Root, "UserConfig").SelectMany(c => Children(c, "UserConfig"));
        foreach (var userConfig in userConfigs)
        {
            if (!Guid.TryParse(Child(userConfig, "UserId")?.Value, out var userId))
            {
                continue;
            }

            var token = Children(userConfig, "UserApiAuth")
                .SelectMany(a => Children(a, "UserApiAuth"))
                .Where(a => string.Equals(Child(a, "Name")?.Value?.Trim(), "AniList", StringComparison.OrdinalIgnoreCase))
                .Select(a => Child(a, "AccessToken")?.Value?.Trim())
                .FirstOrDefault(t => !string.IsNullOrEmpty(t));

            if (token is not null)
            {
                tokens[userId] = token;
            }
        }

        return tokens;
    }

    private static IEnumerable<XElement> Children(XElement? parent, string localName)
        => parent?.Elements().Where(e => e.Name.LocalName == localName) ?? Enumerable.Empty<XElement>();

    private static XElement? Child(XElement parent, string localName) => Children(parent, localName).FirstOrDefault();
}
