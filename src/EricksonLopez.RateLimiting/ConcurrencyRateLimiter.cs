// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Controls the number of concurrent in-flight operations permitted per partition key.
/// </summary>
/// <remarks>
/// <para>
/// Permit acquisition and release within each <see cref="ConcurrencyPartition"/> use lock-free
/// atomic <see cref="System.Threading.Interlocked.CompareExchange(ref int, int, int)"/> operations,
/// guaranteeing bounded latency under high concurrency without spin-wait queuing.
/// </para>
/// <para>
/// Partition lookup via <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
/// uses its own internal locking for structural modifications. Idle partition pruning iterates
/// the full partition set and is not lock-free. These operations occur off the hot-path
/// (only when <see cref="ConcurrencyRateLimiterOptions.MaxPartitions"/> is reached).
/// </para>
/// <para>
/// Disposing an acquired <see cref="RateLimitLease"/> releases allocated permits back to the
/// partition atomically via a one-shot disposer, preventing double-release.
/// </para>
/// </remarks>
public sealed class ConcurrencyRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, ConcurrencyPartition> _partitions = new(StringComparer.Ordinal);
    private readonly ConcurrencyRateLimiterOptions _options;
    private readonly Func<string, ConcurrencyPartition> _createPartition;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyRateLimiter"/> class using the specified options.
    /// </summary>
    /// <param name="options">The configuration options for the rate limiter, or <see langword="null"/> to use default options</param>
    public ConcurrencyRateLimiter(ConcurrencyRateLimiterOptions? options = null)
    {
        _options = options ?? new ConcurrencyRateLimiterOptions();
        _createPartition = _ => new ConcurrencyPartition(_options.PermitLimit);
    }

    /// <inheritdoc/>
    public Task<Result<RateLimitLease>> AcquireAsync(
        string key,
        int permits = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(permits, 1);

        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        if (_partitions.Count >= _options.MaxPartitions)
        {
            PruneIdlePartitions();
            if (_partitions.Count >= _options.MaxPartitions && !_partitions.ContainsKey(key))
            {
                var rejDurationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                RateLimitingMetrics.RecordRequest("concurrency", "rejected", rejDurationMs);
                var rejectedLease = RateLimitLease.Rejected(
                    retryAfter: TimeSpan.FromMilliseconds(50),
                    resetTime: null,
                    limit: _options.PermitLimit);
                return Task.FromResult(Result<RateLimitLease>.Success(rejectedLease));
            }
        }

        while (true)
        {
            var partition = _partitions.GetOrAdd(key, _createPartition);
            var result = partition.TryAcquireEx(permits, out var remainingPermits);

            if (result == ConcurrencyAcquireResult.Retired)
            {
                ((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, ConcurrencyPartition>>)_partitions)
                    .Remove(new System.Collections.Generic.KeyValuePair<string, ConcurrencyPartition>(key, partition));
                continue;
            }

            if (result == ConcurrencyAcquireResult.Acquired)
            {
                var durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                RateLimitingMetrics.RecordRequest("concurrency", "acquired", durationMs);

                var disposer = new OneShotDisposer(partition, permits);
                var lease = RateLimitLease.Successful(
                    remainingPermits,
                    resetTime: null,
                    disposeAction: disposer.Dispose,
                    limit: _options.PermitLimit);

                return Task.FromResult(Result<RateLimitLease>.Success(lease));
            }

            var rejDurationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            RateLimitingMetrics.RecordRequest("concurrency", "rejected", rejDurationMs);

            var rejectedLease = RateLimitLease.Rejected(
                retryAfter: TimeSpan.FromMilliseconds(50),
                resetTime: null,
                limit: _options.PermitLimit);

            return Task.FromResult(Result<RateLimitLease>.Success(rejectedLease));
        }
    }

    private void PruneIdlePartitions()
    {
        foreach (var pair in _partitions)
        {
            if (pair.Value.TryRetire())
            {
                ((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, ConcurrencyPartition>>)_partitions)
                    .Remove(pair);
            }
        }
    }

    private sealed class OneShotDisposer
    {
        private readonly ConcurrencyPartition _partition;
        private readonly int _permits;
        private int _disposed;

        public OneShotDisposer(ConcurrencyPartition partition, int permits)
        {
            _partition = partition;
            _permits = permits;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _partition.Release(_permits);
            }
        }
    }
}
