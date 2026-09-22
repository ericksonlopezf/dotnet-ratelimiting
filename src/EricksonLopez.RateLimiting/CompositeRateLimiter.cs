// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Evaluates multiple child rate limiters in sequential order using conjunction logic.
/// </summary>
/// <remarks>
/// <para>
/// All child limiters must grant a lease for the overall acquisition to succeed.
/// If any child limiter rejects or fails, previously acquired leases in the chain are rolled back.
/// </para>
/// <para>
/// <b>Allocation characteristics:</b> Each <see cref="AcquireAsync"/> call allocates one fixed-size
/// <see cref="RateLimitLease"/> array on the managed heap, sized to the number of child limiters.
/// An additional delegate closure is allocated only when at least one child limiter carries a
/// disposal action (e.g. <see cref="ConcurrencyRateLimiter"/>). This is a deliberate trade-off
/// to support heterogeneous limiter composition; single-algorithm limiters remain fully zero-allocation.
/// </para>
/// </remarks>
public sealed class CompositeRateLimiter : IRateLimiter
{
    private readonly IRateLimiter[] _limiters;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeRateLimiter"/> class with the specified child limiters.
    /// </summary>
    /// <param name="limiters">The ordered sequence of child rate limiters to evaluate</param>
    /// <exception cref="ArgumentNullException"><paramref name="limiters"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="limiters"/> contains no elements</exception>
    public CompositeRateLimiter(IEnumerable<IRateLimiter> limiters)
    {
        ArgumentNullException.ThrowIfNull(limiters);

        var list = new List<IRateLimiter>(limiters);
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one child rate limiter must be specified.", nameof(limiters));
        }

        _limiters = list.ToArray();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeRateLimiter"/> class with child limiters provided as an array.
    /// </summary>
    /// <param name="limiters">The array of child rate limiters to evaluate</param>
    /// <exception cref="ArgumentNullException"><paramref name="limiters"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="limiters"/> contains no elements</exception>
    public CompositeRateLimiter(params IRateLimiter[] limiters)
        : this((IEnumerable<IRateLimiter>)limiters)
    {
    }

    /// <summary>
    /// Gets the ordered collection of child rate limiters evaluated by this instance.
    /// </summary>
    public IReadOnlyList<IRateLimiter> Limiters => _limiters;

    /// <inheritdoc/>
    public async Task<Result<RateLimitLease>> AcquireAsync(
        string key,
        int permits = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(permits, 1);

        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        var acquiredLeases = new RateLimitLease[_limiters.Length];
        int acquiredCount = 0;
        int minRemaining = int.MaxValue;
        int? minLimit = null;
        DateTimeOffset? maxResetTime = null;

        try
        {
            for (int i = 0; i < _limiters.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var limiter = _limiters[i];
                var result = await limiter.AcquireAsync(key, permits, cancellationToken).ConfigureAwait(false);

                if (result.IsFailure)
                {
                    Rollback(acquiredLeases.AsSpan(0, acquiredCount));

                    var errDurationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                    RateLimitingMetrics.RecordRequest("composite", "failed", errDurationMs);
                    return result;
                }

                var lease = result.Value;

                if (!lease.IsAcquired)
                {
                    Rollback(acquiredLeases.AsSpan(0, acquiredCount));

                    var rejDurationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                    RateLimitingMetrics.RecordRequest("composite", "rejected", rejDurationMs);

                    var retryAfter = lease.RetryAfter ?? TimeSpan.FromSeconds(1);
                    return Result<RateLimitLease>.Success(RateLimitLease.Rejected(retryAfter, lease.ResetTime, lease.Limit ?? minLimit));
                }

                acquiredLeases[acquiredCount++] = lease;

                minRemaining = Math.Min(minRemaining, lease.RemainingPermits);

                if (lease.Limit.HasValue)
                {
                    minLimit = minLimit.HasValue ? Math.Min(minLimit.Value, lease.Limit.Value) : lease.Limit.Value;
                }

                if (lease.ResetTime.HasValue)
                {
                    maxResetTime = !maxResetTime.HasValue || lease.ResetTime.Value > maxResetTime.Value ? lease.ResetTime : maxResetTime;
                }
            }
        }
        catch
        {
            Rollback(acquiredLeases.AsSpan(0, acquiredCount));
            throw;
        }

        var durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        RateLimitingMetrics.RecordRequest("composite", "acquired", durationMs);

        Action? combinedDispose = null;
        bool hasDisposeAction = false;
        for (int i = 0; i < acquiredCount; i++)
        {
            if (acquiredLeases[i].DisposeAction != null)
            {
                hasDisposeAction = true;
                break;
            }
        }

        if (hasDisposeAction)
        {
            var disposedFlag = 0;
            combinedDispose = () =>
            {
                if (Interlocked.Exchange(ref disposedFlag, 1) == 0)
                {
                    for (int i = 0; i < acquiredCount; i++)
                    {
                        acquiredLeases[i].Dispose();
                    }
                }
            };
        }

        var finalLease = RateLimitLease.Successful(
            minRemaining == int.MaxValue ? 0 : minRemaining,
            maxResetTime,
            combinedDispose,
            minLimit);

        return Result<RateLimitLease>.Success(finalLease);
    }

    private static void Rollback(ReadOnlySpan<RateLimitLease> leases)
    {
        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }
}
