// Copyright © Erickson Lopez. MIT License.
using System;
using System.Globalization;
using System.Threading.Tasks;
using EricksonLopez.RateLimiting.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EricksonLopez.RateLimiting.AspNetCore;

/// <summary>
/// Enforces HTTP rate limits on incoming requests and injects standard rate limit headers into responses.
/// </summary>
public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitingMiddlewareOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next request processing delegate in the HTTP pipeline</param>
    /// <param name="options">The configuration options accessor for rate limiting behavior</param>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> or <paramref name="options"/> is <see langword="null"/></exception>
    public RateLimitingMiddleware(
        RequestDelegate next,
        IOptions<RateLimitingMiddlewareOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Processes an incoming HTTP request, enforcing rate limits and attaching response headers.
    /// </summary>
    /// <param name="context">The HTTP context for the current request</param>
    /// <param name="rateLimiter">The optional rate limiter instance resolved from dependency injection</param>
    /// <param name="policyRegistry">The optional rate limiter policy registry resolved from dependency injection</param>
    /// <returns>A task representing the asynchronous middleware execution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/></exception>
    /// <exception cref="InvalidOperationException">The specified endpoint rate limiting policy is not registered</exception>
    public async Task InvokeAsync(
        HttpContext context,
        IRateLimiter? rateLimiter = null,
        IRateLimiterPolicyRegistry? policyRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        policyRegistry ??= context.RequestServices?.GetService<IRateLimiterPolicyRegistry>();

        var endpoint = context.GetEndpoint();
        if (endpoint != null)
        {
            if (endpoint.Metadata.GetMetadata<IDisableRateLimitingMetadata>() != null)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            var enableMeta = endpoint.Metadata.GetMetadata<IEnableRateLimitingMetadata>();
            if (enableMeta != null)
            {
                var policyName = enableMeta.PolicyName;
                var policyLimiter = policyRegistry?.GetPolicy(policyName);
                if (policyLimiter == null)
                {
                    throw new InvalidOperationException($"Rate limiting policy '{policyName}' is not registered.");
                }

                rateLimiter = policyLimiter;
            }
        }

        rateLimiter ??= policyRegistry?.DefaultLimiter ?? context.RequestServices?.GetService<IRateLimiter>();
        if (rateLimiter == null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var key = _options.PartitionKeyResolver(context);
        if (string.IsNullOrEmpty(key))
        {
            key = "anonymous";
        }

        var leaseResult = await rateLimiter.AcquireAsync(key, _options.PermitCost, context.RequestAborted).ConfigureAwait(false);

        if (leaseResult.IsFailure)
        {
            if (_options.OnRedisFailure != null)
            {
                await _options.OnRedisFailure(context, leaseResult.Error, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (_options.FailClosed)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";
                var failureJson = $"{{\"code\":\"{leaseResult.Error.Code}\",\"error\":\"{leaseResult.Error.Description}\"}}";
                await context.Response.WriteAsync(failureJson, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        using var lease = leaseResult.Value;
        var response = context.Response;

        if (!response.HasStarted)
        {
            var limitQuota = lease.Limit ?? _options.PermitCost;
            response.Headers[RateLimitingHeaders.Limit] = limitQuota.ToString(CultureInfo.InvariantCulture);
            response.Headers[RateLimitingHeaders.Remaining] = lease.RemainingPermits.ToString(CultureInfo.InvariantCulture);

            if (lease.ResetTime.HasValue)
            {
                response.Headers[RateLimitingHeaders.Reset] = lease.ResetTime.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            }
        }

        if (!lease.IsAcquired)
        {
            var retryAfterSeconds = lease.RetryAfter.HasValue
                ? (int)Math.Ceiling(lease.RetryAfter.Value.TotalSeconds)
                : 1;

            if (!response.HasStarted)
            {
                response.Headers[RateLimitingHeaders.RetryAfter] = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            }

            if (_options.OnRejected != null)
            {
                await _options.OnRejected(context, lease, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            response.StatusCode = StatusCodes.Status429TooManyRequests;
            response.ContentType = "application/json";

            var json = $"{{\"code\":\"RateLimitExceeded\",\"error\":\"Rate limit exceeded. Please retry after {retryAfterSeconds} seconds.\"}}";
            await response.WriteAsync(json, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
