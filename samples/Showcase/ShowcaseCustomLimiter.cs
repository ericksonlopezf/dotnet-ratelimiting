// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.RateLimiting.Showcase;

/// <summary>
/// Represents a custom rate limiter demonstrating extensibility for the showcase application.
/// </summary>
public sealed class ShowcaseCustomLimiter : IRateLimiter
{
    private int _requestCount;

    /// <inheritdoc/>
    public Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);

        var current = Interlocked.Increment(ref _requestCount);
        if (current <= 3)
        {
            return Task.FromResult(Result<RateLimitLease>.Success(
                RateLimitLease.Successful(3 - current, DateTimeOffset.UtcNow.AddMinutes(1), 3)));
        }

        return Task.FromResult(Result<RateLimitLease>.Success(
            RateLimitLease.Rejected(TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow.AddMinutes(1), 3)));
    }
}
