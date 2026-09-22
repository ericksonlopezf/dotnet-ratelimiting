// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.AspNetCore.Builder;

namespace EricksonLopez.RateLimiting.AspNetCore;

/// <summary>
/// Provides extension methods for adding rate limiting metadata to ASP.NET Core endpoint routing conventions.
/// </summary>
public static class EndpointRateLimitingExtensions
{
    /// <summary>
    /// Applies a named rate limiting policy to the endpoint.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type</typeparam>
    /// <param name="builder">The endpoint convention builder</param>
    /// <param name="policyName">The unique identifier of the rate limiting policy to apply</param>
    /// <returns>The endpoint convention builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public static TBuilder RequireRateLimiting<TBuilder>(this TBuilder builder, string policyName)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new EnableRateLimitingAttribute(policyName));

    /// <summary>
    /// Disables rate limiting for the endpoint.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type</typeparam>
    /// <param name="builder">The endpoint convention builder</param>
    /// <returns>The endpoint convention builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    public static TBuilder DisableRateLimiting<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new DisableRateLimitingAttribute());

    /// <summary>
    /// Applies a named rate limiting policy to the endpoint.
    /// </summary>
    /// <remarks>
    /// This method provides backward compatibility and functions identically to <see cref="RequireRateLimiting{TBuilder}"/>.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type</typeparam>
    /// <param name="builder">The endpoint convention builder</param>
    /// <param name="policyName">The unique identifier of the rate limiting policy to apply</param>
    /// <returns>The endpoint convention builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public static TBuilder RequireDistributedRateLimiting<TBuilder>(this TBuilder builder, string policyName)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireRateLimiting(policyName);

    /// <summary>
    /// Disables rate limiting for the endpoint.
    /// </summary>
    /// <remarks>
    /// This method provides backward compatibility and functions identically to <see cref="DisableRateLimiting{TBuilder}"/>.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type</typeparam>
    /// <param name="builder">The endpoint convention builder</param>
    /// <returns>The endpoint convention builder instance to enable method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    public static TBuilder DisableDistributedRateLimiting<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.DisableRateLimiting();
}
