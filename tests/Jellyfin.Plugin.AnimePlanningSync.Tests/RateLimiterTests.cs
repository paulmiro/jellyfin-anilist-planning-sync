using System.Net;
using Jellyfin.Plugin.AnimePlanningSync.AniList;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.AnimePlanningSync.Tests;

public class RateLimiterTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private static HttpResponseMessage Response(HttpStatusCode status, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status);
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    [Fact]
    public async Task SpacesRequestsByConfiguredLimit()
    {
        var limiter = new RateLimiter(_time, () => 20);

        Assert.Equal(TimeSpan.Zero, limiter.GetDelay());
        await limiter.WaitAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(3), limiter.GetDelay());

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(1), limiter.GetDelay());
    }

    [Fact]
    public async Task UsesLowerServerReportedLimit()
    {
        var limiter = new RateLimiter(_time, () => 90);
        limiter.Observe(Response(HttpStatusCode.OK, ("X-RateLimit-Limit", "30"), ("X-RateLimit-Remaining", "29")));

        await limiter.WaitAsync(CancellationToken.None);

        Assert.Equal(30, limiter.EffectiveLimit);
        Assert.Equal(TimeSpan.FromSeconds(2), limiter.GetDelay());
    }

    [Fact]
    public void NeverFasterThanOnePerSecond()
    {
        var limiter = new RateLimiter(_time, () => 1000);
        Assert.Equal(RateLimiter.MinimumSpacing, limiter.Spacing);
    }

    [Fact]
    public void PausesWhenRemainingIsZero()
    {
        var limiter = new RateLimiter(_time, () => 20);
        limiter.Observe(Response(HttpStatusCode.OK, ("X-RateLimit-Remaining", "0")));

        Assert.Equal(TimeSpan.FromMinutes(1), limiter.GetDelay());
    }

    [Fact]
    public void HonoursRetryAfterOn429()
    {
        var limiter = new RateLimiter(_time, () => 20);
        limiter.Observe(Response(HttpStatusCode.TooManyRequests, ("Retry-After", "42")));

        Assert.Equal(TimeSpan.FromSeconds(42), limiter.GetDelay());
    }

    [Fact]
    public void FallsBackToRateLimitResetOn429()
    {
        var limiter = new RateLimiter(_time, () => 20);
        var reset = _time.GetUtcNow().AddSeconds(50).ToUnixTimeSeconds();
        limiter.Observe(Response(HttpStatusCode.TooManyRequests, ("X-RateLimit-Reset", reset.ToString())));

        Assert.Equal(TimeSpan.FromSeconds(50), limiter.GetDelay());
    }

    [Fact]
    public void DefaultsToOneMinuteOn429WithoutHeaders()
    {
        var limiter = new RateLimiter(_time, () => 20);
        limiter.Observe(Response(HttpStatusCode.TooManyRequests));

        Assert.Equal(TimeSpan.FromMinutes(1), limiter.GetDelay());
    }
}
