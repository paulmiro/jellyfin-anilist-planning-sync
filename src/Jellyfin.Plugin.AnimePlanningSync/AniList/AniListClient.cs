using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnimePlanningSync.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AnimePlanningSync.AniList;

/// <summary>
/// Minimal client for the official AniList GraphQL API.
/// All requests from the plugin go through one instance, one at a time, so the rate limiter sees all of them.
/// </summary>
public class AniListClient
{
    internal const string Endpoint = "https://graphql.anilist.co";
    internal const int MaxRetries = 2;
    internal const int ChunkSize = 500;

    private const string ViewerQuery = "query { Viewer { id name } }";

    private const string PlanningQuery = """
        query ($userId: Int, $userName: String, $chunk: Int, $perChunk: Int) {
          MediaListCollection(userId: $userId, userName: $userName, type: ANIME, status: PLANNING,
                              chunk: $chunk, perChunk: $perChunk, sort: [UPDATED_TIME_DESC]) {
            hasNextChunk
            lists { entries { mediaId status updatedAt } }
          }
        }
        """;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AniListClient> _logger;
    private readonly RateLimiter _rateLimiter;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AniListClient(
        IHttpClientFactory httpClientFactory,
        IConfigurationAccessor configuration,
        ILogger<AniListClient> logger)
        : this(httpClientFactory, logger, new RateLimiter(TimeProvider.System, () => configuration.Configuration.MaxRequestsPerMinute))
    {
    }

    internal AniListClient(IHttpClientFactory httpClientFactory, ILogger<AniListClient> logger, RateLimiter rateLimiter)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public async Task<AniListViewer> GetViewerAsync(string token, CancellationToken cancellationToken)
    {
        using var doc = await QueryAsync(ViewerQuery, new Dictionary<string, object?>(), token, cancellationToken).ConfigureAwait(false);
        var viewer = doc.RootElement.GetProperty("data").GetProperty("Viewer");
        if (viewer.ValueKind != JsonValueKind.Object)
        {
            throw new AniListException("AniList did not return the logged in user.");
        }

        return new AniListViewer(viewer.GetProperty("id").GetInt32(), viewer.GetProperty("name").GetString() ?? string.Empty);
    }

    /// <summary>
    /// Fetches only the most recently updated Planning entry. Used to cheaply detect list changes.
    /// </summary>
    public async Task<string?> GetPlanningMarkerAsync(AniListUserRef user, CancellationToken cancellationToken)
    {
        var (_, marker, _) = await GetPlanningChunkAsync(user, 1, 1, cancellationToken).ConfigureAwait(false);
        return marker;
    }

    public async Task<PlanningList> GetPlanningListAsync(AniListUserRef user, CancellationToken cancellationToken)
    {
        var ids = new HashSet<int>();
        string? marker = null;
        var chunk = 1;
        bool hasNext;
        do
        {
            var (chunkIds, chunkMarker, next) = await GetPlanningChunkAsync(user, chunk, ChunkSize, cancellationToken).ConfigureAwait(false);
            ids.UnionWith(chunkIds);
            marker ??= chunkMarker;
            hasNext = next;
            chunk++;
        }
        while (hasNext);

        return new PlanningList(ids, marker);
    }

    private async Task<(List<int> Ids, string? Marker, bool HasNextChunk)> GetPlanningChunkAsync(
        AniListUserRef user, int chunk, int perChunk, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, object?>
        {
            ["chunk"] = chunk,
            ["perChunk"] = perChunk
        };
        if (user.UserId is int id)
        {
            variables["userId"] = id;
        }
        else
        {
            variables["userName"] = user.UserName;
        }

        using var doc = await QueryAsync(PlanningQuery, variables, user.Token, cancellationToken).ConfigureAwait(false);
        var collection = doc.RootElement.GetProperty("data").GetProperty("MediaListCollection");
        if (collection.ValueKind != JsonValueKind.Object)
        {
            throw new AniListException($"AniList returned no list for {user}.");
        }

        var ids = new List<int>();
        var newest = (MediaId: 0, UpdatedAt: -1L);
        // Custom lists are included on purpose: entries can be hidden from the status lists and only show up there.
        foreach (var list in EnumerateArray(collection, "lists"))
        {
            foreach (var entry in EnumerateArray(list, "entries"))
            {
                if (entry.TryGetProperty("status", out var status) && status.GetString() != "PLANNING")
                {
                    continue;
                }

                var mediaId = entry.GetProperty("mediaId").GetInt32();
                ids.Add(mediaId);

                var updatedAt = entry.TryGetProperty("updatedAt", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt64() : 0;
                if (updatedAt > newest.UpdatedAt)
                {
                    newest = (mediaId, updatedAt);
                }
            }
        }

        var marker = newest.UpdatedAt < 0 ? null : $"{newest.MediaId}:{newest.UpdatedAt}";
        var hasNext = collection.TryGetProperty("hasNextChunk", out var h) && h.ValueKind == JsonValueKind.True;
        return (ids, marker, hasNext);
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private async Task<JsonDocument> QueryAsync(
        string query, Dictionary<string, object?> variables, string? token, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = JsonContent.Create(new { query, variables })
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var client = _httpClientFactory.CreateClient(NamedClient.Default);
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                _rateLimiter.Observe(response);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt >= MaxRetries)
                    {
                        throw new AniListException("AniList rate limit exceeded.", response.StatusCode);
                    }

                    _logger.LogWarning("AniList rate limit hit, waiting {Delay} before retrying", _rateLimiter.GetDelay());
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(body);
                }
                catch (JsonException)
                {
                    throw new AniListException($"AniList returned an unexpected response (HTTP {(int)response.StatusCode}).", response.StatusCode);
                }

                var error = GetFirstError(doc);
                if (error is not null || !response.IsSuccessStatusCode)
                {
                    doc.Dispose();
                    throw new AniListException(error ?? $"AniList returned HTTP {(int)response.StatusCode}.", response.StatusCode);
                }

                return doc;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string? GetFirstError(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            var first = errors[0];
            return first.TryGetProperty("message", out var message) ? message.GetString() : "Unknown AniList error";
        }

        return null;
    }
}
