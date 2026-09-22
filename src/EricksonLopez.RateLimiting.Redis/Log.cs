// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.RateLimiting.Redis;

/// <summary>
/// High-performance, zero-allocation log message definitions for the Redis rate limiter.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis rate limiter ACQUIRE failed for key '{Key}'")]
    internal static partial void AcquireFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rate limit acquired for '{Key}': {RemainingPermits} remaining")]
    internal static partial void AcquireSucceeded(ILogger logger, string key, int remainingPermits);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate limit REJECTED for '{Key}': retry after {RetryAfterMs}ms")]
    internal static partial void AcquireRejected(ILogger logger, string key, long retryAfterMs);
}
