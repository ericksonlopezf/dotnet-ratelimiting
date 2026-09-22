// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using Microsoft.AspNetCore.Http;

namespace EricksonLopez.RateLimiting.AspNetCore;

/// <summary>
/// Specifies configuration options for the ASP.NET Core rate limiting middleware.
/// </summary>
public sealed class RateLimitingMiddlewareOptions
{
    /// <summary>
    /// Gets or sets the delegate used to resolve the partition key from an incoming HTTP request.
    /// </summary>
    public Func<HttpContext, string> PartitionKeyResolver { get; set; } = context =>
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

    private int _permitCost = 1;

    /// <summary>
    /// Gets or sets the permit cost evaluated per HTTP request.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1</exception>
    public int PermitCost
    {
        get => _permitCost;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "PermitCost must be at least 1.");
            }
            _permitCost = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether requests should be rejected with a 503 Service Unavailable status when the rate limiter fails.
    /// </summary>
    public bool FailClosed { get; set; }

    /// <summary>
    /// Gets or sets an optional callback invoked when a request is rejected due to rate limiting.
    /// </summary>
    public Func<HttpContext, RateLimitLease, CancellationToken, Task>? OnRejected { get; set; }

    /// <summary>
    /// Gets or sets an optional callback invoked when rate limit evaluation fails due to an infrastructure error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When this callback is defined and a Redis infrastructure failure occurs, the middleware invokes
    /// the callback and then returns immediately — the downstream pipeline (<c>_next</c>) is NOT called.
    /// The callback is therefore responsible for completing the HTTP response (e.g., writing a body or
    /// setting headers), unless Fail-Open behavior is desired. To allow the request to proceed after
    /// logging/alerting, do not define this callback and rely on <see cref="FailClosed"/> instead:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>FailClosed = false</c> (default): Request proceeds to <c>_next</c> after Redis failure.</description></item>
    ///   <item><description><c>FailClosed = true</c>: Request is rejected with HTTP <c>503 Service Unavailable</c>.</description></item>
    ///   <item><description>When <c>OnRedisFailure</c> is set: Callback runs, middleware returns — <c>_next</c> is never called regardless of <c>FailClosed</c>.</description></item>
    /// </list>
    /// <para>
    /// Use <see cref="EricksonLopez.RateLimiting.RateLimitingErrorCodes.ConnectionFailedCode"/> to
    /// programmatically match the error code without relying on a magic string.
    /// </para>
    /// </remarks>
    public Func<HttpContext, Error, CancellationToken, Task>? OnRedisFailure { get; set; }
}
