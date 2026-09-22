// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace EricksonLopez.RateLimiting.Redis;

/// <summary>
/// Provides extension methods for registering Redis-backed distributed rate limiters in an <see cref="IServiceCollection"/>.
/// </summary>
public static class RateLimitingRedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Redis-backed sliding window rate limiter as the <see cref="IRateLimiter"/> service.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="configure">The delegate used to configure <see cref="RedisRateLimiterOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRedisRateLimiting(
        this IServiceCollection services,
        Action<RedisRateLimiterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisRateLimiterOptions>>().Value;
            return ConnectionMultiplexer.Connect(options.Configuration);
        });

        services.AddSingleton<IRateLimiter, RedisSlidingWindowRateLimiter>();

        return services;
    }

    /// <summary>
    /// Adds the Redis-backed sliding window rate limiter using an existing <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="connectionMultiplexer">The existing Redis connection multiplexer instance</param>
    /// <param name="configure">The optional delegate used to configure <see cref="RedisRateLimiterOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="connectionMultiplexer"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRedisRateLimiting(
        this IServiceCollection services,
        IConnectionMultiplexer connectionMultiplexer,
        Action<RedisRateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(connectionMultiplexer);
        services.AddSingleton<IRateLimiter, RedisSlidingWindowRateLimiter>();

        return services;
    }

    /// <summary>
    /// Adds the Redis-backed token bucket rate limiter as the <see cref="IRateLimiter"/> service.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="configure">The delegate used to configure <see cref="RedisTokenBucketRateLimiterOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRedisTokenBucketRateLimiting(
        this IServiceCollection services,
        Action<RedisTokenBucketRateLimiterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisTokenBucketRateLimiterOptions>>().Value;
            return ConnectionMultiplexer.Connect(options.Configuration);
        });

        services.AddSingleton<IRateLimiter, RedisTokenBucketRateLimiter>();

        return services;
    }

    /// <summary>
    /// Adds the Redis-backed token bucket rate limiter using an existing <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="connectionMultiplexer">The existing Redis connection multiplexer instance</param>
    /// <param name="configure">The optional delegate used to configure <see cref="RedisTokenBucketRateLimiterOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="connectionMultiplexer"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRedisTokenBucketRateLimiting(
        this IServiceCollection services,
        IConnectionMultiplexer connectionMultiplexer,
        Action<RedisTokenBucketRateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(connectionMultiplexer);
        services.AddSingleton<IRateLimiter, RedisTokenBucketRateLimiter>();

        return services;
    }
}
