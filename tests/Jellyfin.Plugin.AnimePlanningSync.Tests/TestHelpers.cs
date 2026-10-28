using System.Net;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.AnimePlanningSync.AniList;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Jellyfin.Plugin.AnimePlanningSync.Tests;

/// <summary>
/// Records requests and answers them from a queue of canned responses.
/// </summary>
public class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public void Enqueue(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return response;
        });
    }

    public void EnqueueJson(object body) => Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(body));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("Unexpected request: " + body);
        }

        return _responses.Dequeue()(request);
    }
}

public static class TestHelpers
{
    public static IHttpClientFactory ClientFactory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, false));
        return factory;
    }

    public static (AniListClient Client, FakeTimeProvider Time) CreateClient(FakeHandler handler, int limit = 60)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var limiter = new RateLimiter(time, () => limit);
        return (new AniListClient(ClientFactory(handler), NullLogger<AniListClient>.Instance, limiter), time);
    }

    /// <summary>Advances fake time until the task completes, so rate limiter delays don't block tests.</summary>
    public static async Task<T> RunWithTime<T>(FakeTimeProvider time, Func<Task<T>> action)
    {
        var task = action();
        for (var i = 0; i < 10_000 && !task.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1);
        }

        return await task;
    }

    public static async Task RunWithTime(FakeTimeProvider time, Func<Task> action)
        => await RunWithTime(time, async () => { await action(); return 0; });

    public static object PlanningResponse(bool hasNextChunk, params object[] lists)
        => new { data = new { MediaListCollection = new { hasNextChunk, lists } } };

    public static object List(params object[] entries) => new { entries };

    public static object Entry(int mediaId, long updatedAt, string status = "PLANNING")
        => new { mediaId, status, updatedAt };

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aps-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
