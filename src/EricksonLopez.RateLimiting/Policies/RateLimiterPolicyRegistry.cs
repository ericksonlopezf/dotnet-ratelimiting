// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;

namespace EricksonLopez.RateLimiting.Policies;

/// <summary>
/// Provides a thread-safe registry for storing and resolving named rate limiting policies.
/// </summary>
public sealed class RateLimiterPolicyRegistry : IRateLimiterPolicyRegistry
{
    private readonly ConcurrentDictionary<string, IRateLimiter> _policies = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IRateLimiter? DefaultLimiter { get; set; }

    /// <inheritdoc />
    public void Register(string name, IRateLimiter limiter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(limiter);

        _policies[name] = limiter;
    }

    /// <inheritdoc />
    public IRateLimiter? GetPolicy(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _policies.TryGetValue(name, out var limiter) ? limiter : null;
    }
}
