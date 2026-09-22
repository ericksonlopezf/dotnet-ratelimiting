// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// 1. Register in-memory Sliding Window Rate Limiter
builder.Services.AddSlidingWindowRateLimiter(options =>
{
    options.PermitLimit = 10;
    options.Window = TimeSpan.FromMinutes(1);
    options.SegmentsPerWindow = 6;
});

// 2. Configure HTTP rate limiting middleware options
builder.Services.AddHttpRateLimiting(options =>
{
    options.PermitCost = 1;
    options.PartitionKeyResolver = context =>
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
});

var app = builder.Build();

// 3. Add Rate Limiting Middleware before business endpoints
app.UseHttpRateLimiting();

// Endpoints
app.MapGet("/", () => Results.Ok(new { message = "Welcome to Sliding Window Sample API" }));

app.MapGet("/api/weather", () =>
{
    var forecast = new[]
    {
        new { Date = DateTime.UtcNow.ToShortDateString(), TemperatureC = 25, Summary = "Sunny" }
    };
    return Results.Ok(forecast);
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
   .DisableDistributedRateLimiting();

app.Run();
