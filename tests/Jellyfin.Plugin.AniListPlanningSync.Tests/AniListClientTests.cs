using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.AniListPlanningSync.AniList;
using Xunit;
using static Jellyfin.Plugin.AniListPlanningSync.Tests.TestHelpers;

namespace Jellyfin.Plugin.AniListPlanningSync.Tests;

public class AniListClientTests
{
    private readonly FakeHandler _handler = new();

    [Fact]
    public async Task FetchesAllChunksAndKeepsOnlyPlanningEntries()
    {
        _handler.EnqueueJson(PlanningResponse(
            true,
            List(Entry(1, 300), Entry(2, 200)),
            // A custom list may contain entries of other statuses and entries hidden from the status lists.
            List(Entry(1, 300), Entry(3, 250), Entry(99, 999, "COMPLETED"))));
        _handler.EnqueueJson(PlanningResponse(false, List(Entry(4, 100))));
        var (client, time) = CreateClient(_handler);

        var list = await RunWithTime(time, () => client.GetPlanningListAsync(new AniListUserRef(5, null, "tok"), CancellationToken.None));

        Assert.Equal(new HashSet<int> { 1, 2, 3, 4 }, list.MediaIds);
        Assert.Equal("1:300", list.Marker);
        Assert.Equal(2, _handler.Requests.Count);

        using var second = JsonDocument.Parse(_handler.Requests[1].Body);
        var variables = second.RootElement.GetProperty("variables");
        Assert.Equal(2, variables.GetProperty("chunk").GetInt32());
        Assert.Equal(AniListClient.ChunkSize, variables.GetProperty("perChunk").GetInt32());
        Assert.Equal(5, variables.GetProperty("userId").GetInt32());
        Assert.False(variables.TryGetProperty("userName", out _));
        Assert.Equal("Bearer", _handler.Requests[0].Request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task PublicListRequestsAreUnauthenticated()
    {
        _handler.EnqueueJson(PlanningResponse(false, List(Entry(7, 1))));
        var (client, time) = CreateClient(_handler);

        var marker = await RunWithTime(time, () => client.GetPlanningMarkerAsync(new AniListUserRef(null, "someone", null), CancellationToken.None));

        Assert.Equal("7:1", marker);
        Assert.Null(_handler.Requests[0].Request.Headers.Authorization);
        using var body = JsonDocument.Parse(_handler.Requests[0].Body);
        Assert.Equal("someone", body.RootElement.GetProperty("variables").GetProperty("userName").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("variables").GetProperty("perChunk").GetInt32());
    }

    [Fact]
    public async Task RetriesAfter429()
    {
        _handler.Enqueue(HttpStatusCode.TooManyRequests, """{"data":null,"errors":[{"message":"Too Many Requests.","status":429}]}""",
            r => r.Headers.TryAddWithoutValidation("Retry-After", "30"));
        _handler.EnqueueJson(new { data = new { Viewer = new { id = 1, name = "me" } } });
        var (client, time) = CreateClient(_handler);
        var start = time.GetUtcNow();

        var viewer = await RunWithTime(time, () => client.GetViewerAsync("tok", CancellationToken.None));

        Assert.Equal(new AniListViewer(1, "me"), viewer);
        Assert.Equal(2, _handler.Requests.Count);
        Assert.True(time.GetUtcNow() - start >= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GivesUpAfterRepeated429()
    {
        for (var i = 0; i <= AniListClient.MaxRetries; i++)
        {
            _handler.Enqueue(HttpStatusCode.TooManyRequests, "{}");
        }

        var (client, time) = CreateClient(_handler);

        await Assert.ThrowsAsync<AniListException>(() => RunWithTime(time, () => client.GetViewerAsync("tok", CancellationToken.None)));
        Assert.Equal(AniListClient.MaxRetries + 1, _handler.Requests.Count);
    }

    [Fact]
    public async Task PrivateUserIsAnError()
    {
        _handler.Enqueue(HttpStatusCode.NotFound, """{"errors":[{"message":"Private User","status":404}],"data":{"MediaListCollection":null}}""");
        var (client, time) = CreateClient(_handler);

        var ex = await Assert.ThrowsAsync<AniListException>(
            () => RunWithTime(time, () => client.GetPlanningMarkerAsync(new AniListUserRef(null, "hidden", null), CancellationToken.None)));
        Assert.Equal("Private User", ex.Message);
    }

    [Fact]
    public async Task RequestsAreSpacedByRateLimit()
    {
        _handler.EnqueueJson(PlanningResponse(true, List(Entry(1, 1))));
        _handler.EnqueueJson(PlanningResponse(true, List(Entry(2, 1))));
        _handler.EnqueueJson(PlanningResponse(false, List(Entry(3, 1))));
        var (client, time) = CreateClient(_handler, limit: 20);
        var start = time.GetUtcNow();

        await RunWithTime(time, () => client.GetPlanningListAsync(new AniListUserRef(1, null, null), CancellationToken.None));

        Assert.True(time.GetUtcNow() - start >= TimeSpan.FromSeconds(6));
    }
}
