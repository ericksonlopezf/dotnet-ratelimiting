// Copyright © Erickson Lopez. MIT License.
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Defines rate limiting capabilities for concurrency and throughput throttling.
/// </summary>
/// <remarks>
/// Implementations of this interface should be thread-safe.
/// </remarks>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire the specified number of permits for a partition key.
    /// </summary>
    /// <param name="key">The partition identifier used to isolate rate limit quotas</param>
    /// <param name="permits">The number of permits to acquire. Must be greater than or equal to 1.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains an operation result
    /// encapsulating a <see cref="RateLimitLease"/> indicating whether the permits were granted.
    /// </returns>
    Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default);
}
