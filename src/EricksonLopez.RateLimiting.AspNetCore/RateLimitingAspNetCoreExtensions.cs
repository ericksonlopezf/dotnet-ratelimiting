// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.RateLimiting.Policies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.RateLimiting.AspNetCore;

/// <summary>
/// Provides extension methods for registering and applying rate limiting middleware in ASP.NET Core.
/// </summary>
public static class RateLimitingAspNetCoreExtensions
{
    /// <summary>
    /// Configures HTTP rate limiting options in Microsoft Dependency Injection.
    /// </summary>
    /// <param name="services">The service collection to register options into</param>
    /// <param name="configure">The optional delegate used to configure <see cref="RateLimitingMiddlewareOptions"/></param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddHttpRateLimiting(
        this IServiceCollection services,
        Action<RateLimitingMiddlewareOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure != null)
        {
            services.Configure(configure);
        }

        return services;
    }

    /// <summary>
    /// Registers named rate limiting policies using a fluent builder and configures middleware services.
    /// </summary>
    /// <param name="services">The service collection to register services into</param>
    /// <param name="configure">The delegate used to configure named rate limiting policies</param>
    /// <param name="configureMiddleware">The optional delegate used to configure middleware options</param>
    /// <returns>The same service collection instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRateLimiting(
        this IServiceCollection services,
        Action<RateLimiterPolicyBuilder> configure,
        Action<RateLimitingMiddlewareOptions>? configureMiddleware = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new RateLimiterPolicyBuilder();
        configure(builder);
        var registry = builder.Build();

        services.AddSingleton<IRateLimiterPolicyRegistry>(registry);

        if (registry.DefaultLimiter != null)
        {
            services.TryAddSingleton(registry.DefaultLimiter);
        }

        if (configureMiddleware != null)
        {
            services.Configure(configureMiddleware);
        }

        return services;
    }

    /// <summary>
    /// Adds <see cref="RateLimitingMiddleware"/> into the application request execution pipeline.
    /// </summary>
    /// <param name="app">The application pipeline builder</param>
    /// <returns>The application builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/></exception>
    public static IApplicationBuilder UseHttpRateLimiting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<RateLimitingMiddleware>();
    }
}
