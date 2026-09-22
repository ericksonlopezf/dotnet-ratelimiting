// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Provides extension methods for registering rate limiter services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="SlidingWindowRateLimiter"/> as the default <see cref="IRateLimiter"/> service.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="configure">The optional delegate used to configure <see cref="RateLimiterOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddSlidingWindowRateLimiter(
        this IServiceCollection services,
        Action<RateLimiterOptions>? configure = null)
    {
        var options = new RateLimiterOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IRateLimiter, SlidingWindowRateLimiter>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="TokenBucketRateLimiter"/> as the default <see cref="IRateLimiter"/> service.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="configure">The optional delegate used to configure <see cref="RateLimiterOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddTokenBucketRateLimiter(
        this IServiceCollection services,
        Action<RateLimiterOptions>? configure = null)
    {
        var options = new RateLimiterOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IRateLimiter, TokenBucketRateLimiter>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="FixedWindowRateLimiter"/> as the default <see cref="IRateLimiter"/> service.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="configure">The optional delegate used to configure <see cref="RateLimiterOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddFixedWindowRateLimiter(
        this IServiceCollection services,
        Action<RateLimiterOptions>? configure = null)
    {
        var options = new RateLimiterOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IRateLimiter, FixedWindowRateLimiter>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="ConcurrencyRateLimiter"/> as the default <see cref="IRateLimiter"/> service.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="configure">The optional delegate used to configure <see cref="ConcurrencyRateLimiterOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddConcurrencyRateLimiter(
        this IServiceCollection services,
        Action<ConcurrencyRateLimiterOptions>? configure = null)
    {
        var options = new ConcurrencyRateLimiterOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IRateLimiter, ConcurrencyRateLimiter>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="CompositeRateLimiter"/> as the default <see cref="IRateLimiter"/> service.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="limiters">The ordered sequence of child rate limiters to evaluate</param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="limiters"/> is <see langword="null"/></exception>
    public static IServiceCollection AddCompositeRateLimiter(
        this IServiceCollection services,
        params IRateLimiter[] limiters)
    {
        services.AddSingleton<IRateLimiter>(new CompositeRateLimiter(limiters));

        return services;
    }
}
