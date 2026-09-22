// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.Json;
using System.Threading.Tasks;
using EricksonLopez.RateLimiting.AspNetCore;
using EricksonLopez.RateLimiting.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRedisRateLimiting(options =>
{
    // Intentionally configured to an unreachable port to simulate Redis outage
    options.Configuration = "127.0.0.1:6399,abortConnect=false,connectTimeout=500";
    options.KeyPrefix = "failopen:sample:";
    options.MaxPermits = 5;
    options.WindowDuration = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpRateLimiting(options =>
{
    // High-availability default: fail-open (allow requests to proceed if Redis fails)
    options.FailClosed = false;

    // Custom operational hook executed when Redis connection/command fails
    options.OnRedisFailure = async (context, error, ct) =>
    {
        var logger = context.RequestServices.GetService(typeof(ILogger<Program>)) as ILogger<Program>;
        logger?.LogWarning("Redis infrastructure degraded: {ErrorCode} - {ErrorDetail}. Request allowed via Fail-Open.",
            error.Code, error.Description);

        // Append a diagnostic header so clients and upstream gateways know rate limiting was bypassed
        context.Response.Headers["X-RateLimit-Degraded"] = "true";
        await Task.CompletedTask;
    };

    // Custom rejection response in RFC 7807 ProblemDetails format
    options.OnRejected = async (context, lease, ct) =>
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/problem+json";

        var retryAfter = lease.RetryAfter.HasValue ? (int)Math.Ceiling(lease.RetryAfter.Value.TotalSeconds) : 1;
        var problem = new
        {
            type = "https://httpstatuses.com/429",
            title = "Too Many Requests",
            status = 429,
            detail = $"Quota exceeded. Retry allowed in {retryAfter} seconds.",
            instance = context.Request.Path.ToString()
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem), ct);
    };
});

var app = builder.Build();

app.UseHttpRateLimiting();

app.MapGet("/api/resilient-endpoint", () => Results.Ok(new
{
    message = "Request served successfully even if Redis is unreachable!"
}));

app.Run();
