// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.RateLimiting.AspNetCore;

/// <summary>
/// Defines standard HTTP header constants used in rate limiting responses.
/// </summary>
public static class RateLimitingHeaders
{
    /// <summary>
    /// Specifies the header name for the maximum number of requests permitted in the current time window.
    /// </summary>
    public const string Limit = "X-RateLimit-Limit";

    /// <summary>
    /// Specifies the header name for the number of requests remaining in the current time window.
    /// </summary>
    public const string Remaining = "X-RateLimit-Remaining";

    /// <summary>
    /// Specifies the header name for the UNIX timestamp when the current rate limit window resets.
    /// </summary>
    public const string Reset = "X-RateLimit-Reset";

    /// <summary>
    /// Specifies the standard HTTP header name for the retry interval after a rejected request.
    /// </summary>
    public const string RetryAfter = "Retry-After";
}
