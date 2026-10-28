using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AnimePlanningSync.AniList;

/// <summary>
/// Spaces requests evenly so that no more than the allowed number are sent per minute,
/// and backs off according to the rate limit headers AniList returns.
/// Not thread-safe on its own; callers must serialize access.
/// </summary>
public class RateLimiter
{
    internal static readonly TimeSpan MinimumSpacing = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _timeProvider;
    private readonly Func<int> _configuredLimit;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
    private int? _serverLimit;

    public RateLimiter(TimeProvider timeProvider, Func<int> configuredLimit)
    {
        _timeProvider = timeProvider;
        _configuredLimit = configuredLimit;
    }

    public int EffectiveLimit
    {
        get
        {
            var limit = Math.Max(1, _configuredLimit());
            return _serverLimit is int server && server > 0 ? Math.Min(limit, server) : limit;
        }
    }

    public TimeSpan Spacing
    {
        get
        {
            var spacing = TimeSpan.FromMinutes(1) / EffectiveLimit;
            return spacing < MinimumSpacing ? MinimumSpacing : spacing;
        }
    }

    public TimeSpan GetDelay()
    {
        var now = _timeProvider.GetUtcNow();
        var next = _lastRequest == DateTimeOffset.MinValue ? now : _lastRequest + Spacing;
        if (_blockedUntil > next)
        {
            next = _blockedUntil;
        }

        return next > now ? next - now : TimeSpan.Zero;
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        var delay = GetDelay();
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        _lastRequest = _timeProvider.GetUtcNow();
    }

    public void Observe(HttpResponseMessage response)
    {
        var now = _timeProvider.GetUtcNow();

        if (TryGetInt(response, "X-RateLimit-Limit", out var limit))
        {
            _serverLimit = limit;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _blockedUntil = Max(_blockedUntil, now + GetRetryAfter(response, now));
            return;
        }

        if (TryGetInt(response, "X-RateLimit-Remaining", out var remaining) && remaining <= 0)
        {
            _blockedUntil = Max(_blockedUntil, now + DefaultTimeout);
        }
    }

    internal static TimeSpan GetRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (TryGetInt(response, "Retry-After", out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (TryGetLong(response, "X-RateLimit-Reset", out var reset))
        {
            var wait = DateTimeOffset.FromUnixTimeSeconds(reset) - now;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return DefaultTimeout;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static bool TryGetInt(HttpResponseMessage response, string header, out int value)
    {
        value = 0;
        return TryGetHeader(response, header, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetLong(HttpResponseMessage response, string header, out long value)
    {
        value = 0;
        return TryGetHeader(response, header, out var raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetHeader(HttpResponseMessage response, string header, out string? value)
    {
        value = null;
        if (response.Headers.TryGetValues(header, out var values))
        {
            value = values.FirstOrDefault();
        }

        return value is not null;
    }
}
