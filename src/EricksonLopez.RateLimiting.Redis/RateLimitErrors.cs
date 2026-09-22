// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.RateLimiting;
using EricksonLopez.Result;

namespace EricksonLopez.RateLimiting.Redis;

/// <summary>
/// Provides factory methods for error definitions associated with Redis rate limiter operations.
/// </summary>
internal static class RateLimitErrors
{
    /// <summary>
    /// Creates an error representing a Redis connection or command execution failure.
    /// </summary>
    /// <param name="detail">The detailed explanation of the failure</param>
    /// <returns>A new <see cref="Error"/> representing the failure condition.</returns>
    internal static Error ConnectionFailed(string detail) =>
        Error.Failure(RateLimitingErrorCodes.ConnectionFailedCode, $"Redis operation failed: {detail}");
}
