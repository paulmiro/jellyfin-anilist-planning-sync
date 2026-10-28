using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListPlanningSync.Mapping;

/// <summary>
/// Keeps a local copy of Fribb/anime-lists' id mapping file. It is downloaded at most once per refresh
/// (conditional request via ETag) and all lookups happen locally.
/// </summary>
public class AnimeMappingStore
{
    internal const string MappingUrl = "https://raw.githubusercontent.com/Fribb/anime-lists/master/anime-list-full.json";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnimeMappingStore> _logger;
    private readonly string _filePath;
    private readonly string _etagPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private MappingIndex? _index;

    public AnimeMappingStore(IHttpClientFactory httpClientFactory, IApplicationPaths applicationPaths, ILogger<AnimeMappingStore> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        var directory = Path.Combine(applicationPaths.CachePath, "anilist-planning-sync");
        _filePath = Path.Combine(directory, "anime-list-full.json");
        _etagPath = _filePath + ".etag";
    }

    /// <summary>
    /// Returns the loaded index, loading it from disk or downloading it first if needed. Returns null if unavailable.
    /// </summary>
    public async Task<MappingIndex?> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (_index is not null)
        {
            return _index;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_index is null && !File.Exists(_filePath))
            {
                await DownloadAsync(cancellationToken).ConfigureAwait(false);
            }

            _index ??= Load();
            return _index;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await DownloadAsync(cancellationToken).ConfigureAwait(false) || _index is null)
            {
                _index = Load() ?? _index;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <returns>True if a new file was downloaded.</returns>
    private async Task<bool> DownloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            using var request = new HttpRequestMessage(HttpMethod.Get, MappingUrl);
            if (File.Exists(_filePath) && File.Exists(_etagPath))
            {
                var etag = await File.ReadAllTextAsync(_etagPath, cancellationToken).ConfigureAwait(false);
                if (EntityTagHeaderValue.TryParse(etag, out var parsed))
                {
                    request.Headers.IfNoneMatch.Add(parsed);
                }
            }

            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _logger.LogDebug("Anime mapping file is up to date");
                return false;
            }

            response.EnsureSuccessStatusCode();

            var tempPath = _filePath + ".tmp";
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var file = File.Create(tempPath);
                await using (file.ConfigureAwait(false))
                {
                    await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                }
            }

            // Validate before replacing the previous copy.
            using (var check = File.OpenRead(tempPath))
            {
                MappingIndex.Parse(check);
            }

            File.Move(tempPath, _filePath, true);
            if (response.Headers.ETag is not null)
            {
                await File.WriteAllTextAsync(_etagPath, response.Headers.ETag.ToString(), cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Downloaded anime mapping file");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not download anime mapping file from {Url}", MappingUrl);
            return false;
        }
    }

    private MappingIndex? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            var index = MappingIndex.Parse(stream);
            _logger.LogDebug("Loaded anime mapping file with {Count} entries", index.Count);
            return index;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse anime mapping file {Path}", _filePath);
            return null;
        }
    }
}
