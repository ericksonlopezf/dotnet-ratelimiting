// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting.Policies;

/// <summary>
/// Represents a named rate limiting policy.
/// </summary>
public sealed class RateLimiterPolicy : IRateLimiterPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiterPolicy"/> class with the specified name and rate limiter.
    /// </summary>
    /// <param name="name">The unique identifier of the policy</param>
    /// <param name="limiter">The rate limiter implementation associated with the policy</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="limiter"/> is <see langword="null"/></exception>
    public RateLimiterPolicy(string name, IRateLimiter limiter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IRateLimiter Limiter { get; }
}
