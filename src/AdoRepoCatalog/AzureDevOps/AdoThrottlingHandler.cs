using System.Globalization;
using System.Net;
using AdoRepoCatalog.Infrastructure;

namespace AdoRepoCatalog.AzureDevOps;

/// <summary>
/// Shared ADO throttle: bounded in-flight HTTP, 429 backoff, and delay headers
/// even when the status is 200. One configured credential — no token rotation.
/// </summary>
public sealed class AdoThrottlingHandler : DelegatingHandler
{
    public const string RateLimitDelayHeader = "X-RateLimit-Delay";
    public const string RateLimitRemainingHeader = "X-RateLimit-Remaining";

    private readonly CatalogOptions _options;
    private readonly IAsyncDelay _delay;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _concurrency;
    private readonly object _sync = new();
    private TimeSpan _pendingDelay = TimeSpan.Zero;

    public AdoThrottlingHandler(
        CatalogOptions options,
        IAsyncDelay? delay = null,
        TimeProvider? timeProvider = null,
        HttpMessageHandler? innerHandler = null)
    {
        _options = options;
        _delay = delay ?? SystemAsyncDelay.Instance;
        _clock = timeProvider ?? TimeProvider.System;
        _concurrency = new SemaphoreSlim(options.EffectiveMaxConcurrency, options.EffectiveMaxConcurrency);
        if (innerHandler is not null)
        {
            InnerHandler = innerHandler;
        }
    }

    public int EffectiveMaxConcurrency => _options.EffectiveMaxConcurrency;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FlushPendingDelayAsync(cancellationToken).ConfigureAwait(false);

            HttpResponseMessage? response = null;
            for (var attempt = 0; ; attempt++)
            {
                response?.Dispose();
                response = await base.SendAsync(Clone(request), cancellationToken).ConfigureAwait(false);
                RecordDelayHints(response);

                if (response.StatusCode != HttpStatusCode.TooManyRequests ||
                    attempt >= Math.Max(0, _options.MaxRetries))
                {
                    return response;
                }

                var wait = ReadRetryAfter(response) ?? ComputeBackoff(attempt);
                response.Dispose();
                response = null;
                await _delay.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _concurrency.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _concurrency.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task FlushPendingDelayAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (_sync)
        {
            wait = _pendingDelay;
            _pendingDelay = TimeSpan.Zero;
        }

        if (wait > TimeSpan.Zero)
        {
            await _delay.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private void RecordDelayHints(HttpResponseMessage response)
    {
        var hinted = ReadRateLimitDelay(response) ?? ReadRetryAfter(response);
        if (hinted is null && RemainingIsZero(response))
        {
            hinted = TimeSpan.FromSeconds(1);
        }

        if (hinted is not { } wait || wait <= TimeSpan.Zero)
        {
            return;
        }

        lock (_sync)
        {
            if (wait > _pendingDelay)
            {
                _pendingDelay = wait;
            }
        }
    }

    public TimeSpan ComputeBackoff(int attempt)
    {
        var ticks = _options.RetryBaseDelay.Ticks * (1L << Math.Clamp(attempt, 0, 6));
        var backoff = TimeSpan.FromTicks(Math.Min(ticks, TimeSpan.FromSeconds(30).Ticks));
        if (_options.RetryJitterRatio <= 0)
        {
            return backoff;
        }

        var jitter = backoff.TotalMilliseconds * _options.RetryJitterRatio * Random.Shared.NextDouble();
        return backoff + TimeSpan.FromMilliseconds(jitter);
    }

    public TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - _clock.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values) &&
            TryParseSeconds(values.FirstOrDefault(), out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    public static TimeSpan? ReadRateLimitDelay(HttpResponseMessage response)
    {
        if (!TryGetHeader(response, RateLimitDelayHeader, out var raw))
        {
            return null;
        }

        return TryParseSeconds(raw, out var seconds) ? TimeSpan.FromSeconds(seconds) : null;
    }

    public static bool RemainingIsZero(HttpResponseMessage response)
        => TryGetHeader(response, RateLimitRemainingHeader, out var raw) &&
           int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining) &&
           remaining <= 0;

    private static bool TryGetHeader(HttpResponseMessage response, string name, out string value)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            value = values.FirstOrDefault() ?? "";
            return !string.IsNullOrWhiteSpace(value);
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            value = contentValues.FirstOrDefault() ?? "";
            return !string.IsNullOrWhiteSpace(value);
        }

        value = "";
        return false;
    }

    private static bool TryParseSeconds(string? raw, out double seconds)
    {
        seconds = 0;
        return !string.IsNullOrWhiteSpace(raw) &&
               double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
               seconds >= 0;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Version = request.Version;
        return clone;
    }
}
