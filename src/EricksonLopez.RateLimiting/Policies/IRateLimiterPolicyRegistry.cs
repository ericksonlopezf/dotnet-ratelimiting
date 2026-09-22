// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.RateLimiting.Policies;

/// <summary>
/// Defines a registry for managing and resolving named rate limiting policies.
/// </summary>
public interface IRateLimiterPolicyRegistry
{
    /// <summary>
    /// Gets or sets the default fallback rate limiter used when no policy is explicitly assigned.
    /// </summary>
    IRateLimiter? DefaultLimiter { get; set; }

    /// <summary>
    /// Registers a named policy with its rate limiter instance.
    /// </summary>
    /// <param name="name">The unique identifier of the policy</param>
    /// <param name="limiter">The rate limiter instance to associate with the policy name</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="limiter"/> is <see langword="null"/></exception>
    void Register(string name, IRateLimiter limiter);

    /// <summary>
    /// Retrieves a rate limiter for the specified policy name.
    /// </summary>
    /// <param name="name">The unique identifier of the policy to retrieve</param>
    /// <returns>The <see cref="IRateLimiter"/> associated with the policy name, or <see langword="null"/> if not registered.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    IRateLimiter? GetPolicy(string name);
}
