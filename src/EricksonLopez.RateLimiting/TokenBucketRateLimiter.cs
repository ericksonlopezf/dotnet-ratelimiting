// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Controls request rates using an in-memory continuous token bucket replenishment algorithm.
/// </summary>
/// <remarks>
/// Permits refill continuously over time based on the configured rate limit capacity and window duration.
/// This type is thread-safe.
/// </remarks>
public sealed class TokenBucketRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, TokenBucketPartition> _partitions = new(StringComparer.Ordinal);
    private readonly RateLimiterOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenBucketRateLimiter"/> class with the specified options and time provider.
    /// </summary>
    /// <param name="options">The configuration options for the rate limiter, or <see langword="null"/> to use default options</param>
    /// <param name="timeProvider">The time provider used for replenishment calculations, or <see langword="null"/> to use <see cref="TimeProvider.System"/></param>
    public TokenBucketRateLimiter(RateLimiterOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new RateLimiterOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(permits, 1);

        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var now = _timeProvider.GetUtcNow();

        if (_partitions.Count >= _options.MaxPartitions && !_partitions.ContainsKey(key))
        {
            PruneIdlePartitions(now);
            if (_partitions.Count >= _options.MaxPartitions && !_partitions.ContainsKey(key))
            {
                var rejDurationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                RateLimitingMetrics.RecordRequest("token_bucket", "rejected", rejDurationMs);
                var rejectLease = RateLimitLease.Rejected(_options.Window, now.Add(_options.Window), _options.PermitLimit);
                return Task.FromResult(Result<RateLimitLease>.Success(rejectLease));
            }
        }

        var partition = _partitions.GetOrAdd(
            key,
            _ => new TokenBucketPartition(_options.PermitLimit, _options.Window, now));

        var lease = partition.TryAcquire(permits, now);
        var durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        RateLimitingMetrics.RecordRequest("token_bucket", lease.IsAcquired ? "acquired" : "rejected", durationMs);
        return Task.FromResult(Result<RateLimitLease>.Success(lease));
    }

    private void PruneIdlePartitions(DateTimeOffset now)
    {
        foreach (var pair in _partitions)
        {
            if (pair.Value.IsIdle(now))
            {
                _partitions.TryRemove(pair.Key, out _);
            }
        }
    }
}
