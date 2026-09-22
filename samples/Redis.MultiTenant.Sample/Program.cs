// Copyright © Erickson Lopez. MIT License.
using System;
using System.Security.Claims;
using EricksonLopez.RateLimiting.AspNetCore;
using EricksonLopez.RateLimiting.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// 1. Register distributed Redis-backed rate limiter
builder.Services.AddRedisRateLimiting(options =>
{
    options.Configuration = builder.Configuration["Redis:Configuration"] ?? "localhost:6379,abortConnect=false";
    options.KeyPrefix = "sample:ratelimit:";
    options.MaxPermits = 100;
    options.WindowDuration = TimeSpan.FromMinutes(1);
});

// 2. Configure hierarchical multi-tenant partition key resolver
// Identity Hierarchy: TenantId -> Authenticated User (SubjectId) -> Client IP Address
builder.Services.AddHttpRateLimiting(options =>
{
    options.PermitCost = 1;
    options.PartitionKeyResolver = context =>
    {
        // 1. Tenant header (e.g. multi-tenant SaaS)
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantId) && !string.IsNullOrWhiteSpace(tenantId))
        {
            return $"tenant:{tenantId}";
        }

        // 2. Authenticated user Subject ID
        var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(sub))
        {
            return $"user:{sub}";
        }

        // 3. Fallback to IP address
        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";
    };
});

var app = builder.Build();

app.UseHttpRateLimiting();

app.MapGet("/api/tenant-data", (HttpContext context) =>
{
    var tenant = context.Request.Headers["X-Tenant-Id"].ToString();
    return Results.Ok(new
    {
        Tenant = string.IsNullOrEmpty(tenant) ? "Default" : tenant,
        Data = "Confidential business data protected by distributed rate limits."
    });
});

app.Run();
