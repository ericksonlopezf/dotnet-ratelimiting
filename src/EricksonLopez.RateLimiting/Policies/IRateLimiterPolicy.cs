// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.RateLimiting.Policies;

/// <summary>
/// Defines a named rate limiting policy bound to a rate limiter instance.
/// </summary>
public interface IRateLimiterPolicy
{
    /// <summary>
    /// Gets the unique identifier of the policy.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the rate limiter instance associated with the policy.
    /// </summary>
    IRateLimiter Limiter { get; }
}
