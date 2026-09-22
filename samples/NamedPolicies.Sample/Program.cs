// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.RateLimiting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register multiple Named Policies via fluent builder
builder.Services.AddRateLimiting(policies =>
{
    // Fixed Window policy for public tier (5 requests per minute)
    policies.AddFixedWindow("public-tier", options =>
    {
        options.PermitLimit = 5;
        options.Window = TimeSpan.FromMinutes(1);
    });

    // Sliding Window policy for authenticated tier (30 requests per minute)
    policies.AddSlidingWindow("auth-tier", options =>
    {
        options.PermitLimit = 30;
        options.Window = TimeSpan.FromMinutes(1);
        options.SegmentsPerWindow = 6;
    });

    // Token Bucket for high-volume streaming/export tier
    policies.AddTokenBucket("streaming-tier", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromSeconds(10);
    });

    // Default policy applied when an endpoint does not explicitly request one
    policies.SetDefaultPolicy("public-tier");
});

var app = builder.Build();

app.UseHttpRateLimiting();

// 1. Endpoint using the public-tier policy explicitly
app.MapGet("/api/public", () => Results.Ok(new { tier = "public", limit = "5 req/min" }))
   .RequireDistributedRateLimiting("public-tier");

// 2. Endpoint using the auth-tier policy
app.MapGet("/api/member", () => Results.Ok(new { tier = "member", limit = "30 req/min" }))
   .RequireDistributedRateLimiting("auth-tier");

// 3. Endpoint using the streaming-tier policy
app.MapGet("/api/export", () => Results.Ok(new { tier = "streaming", limit = "100 req/10s" }))
   .RequireDistributedRateLimiting("streaming-tier");

// 4. Exempt endpoint with rate limiting disabled
app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy", rateLimiting = "Bypassed" }))
   .DisableDistributedRateLimiting();

// 5. Default endpoint (inherits default policy: public-tier)
app.MapGet("/api/default", () => Results.Ok(new { tier = "default (public-tier)" }));

app.Run();
