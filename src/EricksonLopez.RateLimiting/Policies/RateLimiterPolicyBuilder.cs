// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting.Policies;

/// <summary>
/// Provides a fluent builder for configuring and registering named rate limiting policies.
/// </summary>
public sealed class RateLimiterPolicyBuilder
{
    private readonly RateLimiterPolicyRegistry _registry = new();

    /// <summary>
    /// Configures and registers a fixed window rate limiting policy.
    /// </summary>
    /// <param name="policyName">The unique identifier of the policy</param>
    /// <param name="configure">The delegate used to configure rate limiter options</param>
    /// <param name="timeProvider">The optional time provider used for window calculations</param>
    /// <returns>This builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/></exception>
    public RateLimiterPolicyBuilder AddFixedWindow(
        string policyName,
        Action<RateLimiterOptions> configure,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RateLimiterOptions();
        configure(options);

        var limiter = new FixedWindowRateLimiter(options, timeProvider);
        _registry.Register(policyName, limiter);
        return this;
    }

    /// <summary>
    /// Configures and registers a sliding window rate limiting policy.
    /// </summary>
    /// <param name="policyName">The unique identifier of the policy</param>
    /// <param name="configure">The delegate used to configure rate limiter options</param>
    /// <param name="timeProvider">The optional time provider used for window calculations</param>
    /// <returns>This builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/></exception>
    public RateLimiterPolicyBuilder AddSlidingWindow(
        string policyName,
        Action<RateLimiterOptions> configure,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RateLimiterOptions();
        configure(options);

        var limiter = new SlidingWindowRateLimiter(options, timeProvider);
        _registry.Register(policyName, limiter);
        return this;
    }

    /// <summary>
    /// Configures and registers a token bucket rate limiting policy.
    /// </summary>
    /// <param name="policyName">The unique identifier of the policy</param>
    /// <param name="configure">The delegate used to configure rate limiter options</param>
    /// <param name="timeProvider">The optional time provider used for replenishment calculations</param>
    /// <returns>This builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/></exception>
    public RateLimiterPolicyBuilder AddTokenBucket(
        string policyName,
        Action<RateLimiterOptions> configure,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RateLimiterOptions();
        configure(options);

        var limiter = new TokenBucketRateLimiter(options, timeProvider);
        _registry.Register(policyName, limiter);
        return this;
    }

    /// <summary>
    /// Configures and registers a concurrency rate limiting policy.
    /// </summary>
    /// <param name="policyName">The unique identifier of the policy</param>
    /// <param name="configure">The delegate used to configure concurrency rate limiter options</param>
    /// <returns>This builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/></exception>
    public RateLimiterPolicyBuilder AddConcurrency(
        string policyName,
        Action<ConcurrencyRateLimiterOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ConcurrencyRateLimiterOptions();
        configure(options);

        var limiter = new ConcurrencyRateLimiter(options);
        _registry.Register(policyName, limiter);
        return this;
    }

    /// <summary>
    /// Configures and registers a composite rate limiting policy evaluated with conjunction logic.
    /// </summary>
    /// <param name="policyName">The unique identifier of the policy</param>
    /// <param name="limiters">The ordered sequence of child rate limiters</param>
    /// <returns>This builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="limiters"/> is <see langword="null"/></exception>
    public RateLimiterPolicyBuilder AddComposite(
        string policyName,
        params IRateLimiter[] limiters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentNullException.ThrowIfNull(limiters);

        var limiter = new CompositeRateLimiter(limiters);
        _registry.Register(policyName, limiter);
        return this;
    }

    /// <summary>
    /// Registers an arbitrary pre-instantiated rate limiter under the specified policy name.
    /// </summary>
    /// <param name="policyName">The unique identifier of the policy</param>
    /// <param name="limiter">The rate limiter instance to associate with the policy</param>
    /// <returns>This builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="limiter"/> is <see langword="null"/></exception>
    public RateLimiterPolicyBuilder AddPolicy(string policyName, IRateLimiter limiter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentNullException.ThrowIfNull(limiter);

        _registry.Register(policyName, limiter);
        return this;
    }

    /// <summary>
    /// Sets an explicit rate limiter instance as the default fallback policy.
    /// </summary>
    /// <param name="limiter">The default rate limiter instance</param>
    /// <returns>This builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="limiter"/> is <see langword="null"/></exception>
    public RateLimiterPolicyBuilder SetDefaultPolicy(IRateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        _registry.DefaultLimiter = limiter;
        return this;
    }

    /// <summary>
    /// Sets an existing named policy as the default fallback policy.
    /// </summary>
    /// <param name="policyName">The unique identifier of the registered policy</param>
    /// <returns>This builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="InvalidOperationException">The specified policy name is not registered</exception>
    public RateLimiterPolicyBuilder SetDefaultPolicy(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        var limiter = _registry.GetPolicy(policyName)
            ?? throw new InvalidOperationException($"Policy '{policyName}' is not registered.");

        _registry.DefaultLimiter = limiter;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured policy registry.
    /// </summary>
    /// <returns>The populated <see cref="IRateLimiterPolicyRegistry"/> containing all registered policies.</returns>
    public IRateLimiterPolicyRegistry Build() => _registry;
}
