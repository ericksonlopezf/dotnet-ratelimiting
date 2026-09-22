// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Provides standardized error code constants for rate limiting failure conditions.
/// </summary>
/// <remarks>
/// Use these constants in error callbacks to perform programmatic pattern matching without relying on magic strings.
/// </remarks>
/// <example>
/// <code>
/// options.OnRedisFailure = async (context, error, ct) =>
/// {
///     if (error.Code == RateLimitingErrorCodes.ConnectionFailedCode)
///     {
///         // Handle Redis connection failure specifically
///         context.Response.Headers["X-RateLimit-Degraded"] = "true";
///     }
///     await Task.CompletedTask;
/// };
/// </code>
/// </example>
public static class RateLimitingErrorCodes
{
    /// <summary>
    /// Defines the error code emitted when a distributed cache operation fails due to a connection error, timeout, or network partition.
    /// </summary>
    public const string ConnectionFailedCode = "RateLimit.Redis.ConnectionFailed";
}
