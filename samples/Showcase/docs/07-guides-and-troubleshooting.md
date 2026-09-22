# Phase 8 · Operations, Migration, Best Practices, and Troubleshooting Guide

This document gathers official operational guides for adopting and maintaining `EricksonLopez.RateLimiting`.

---

## 1. Quick Start (5 Minutes)

### Step 1: Package Installation
```bash
dotnet add package EricksonLopez.RateLimiting
dotnet add package EricksonLopez.RateLimiting.AspNetCore
```

### Step 2: Setup in `Program.cs`
```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Register fixed window rate limiter: 60 requests per minute per IP
builder.Services.AddFixedWindowRateLimiter(options =>
{
    options.PermitLimit = 60;
    options.Window = TimeSpan.FromMinutes(1);
});

builder.Services.AddHttpRateLimiting();

var app = builder.Build();

app.UseHttpRateLimiting();

app.MapGet("/api/ping", () => "pong");

app.Run();
```

---

## 2. Comprehensive Production Setup

For enterprise production applications running on Kubernetes with distributed Redis and multiple client profiles:

```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;
using EricksonLopez.RateLimiting.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Distributed Redis configuration for Kubernetes pods
builder.Services.AddRedisRateLimiting(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
    options.KeyPrefix = "production:api:";
    options.MaxPermits = 120;
    options.WindowDuration = TimeSpan.FromMinutes(1);
});

// 2. Middleware with resilience policies
builder.Services.AddHttpRateLimiting(options =>
{
    options.FailClosed = false; // Keep serving traffic if Redis fails
    options.PartitionKeyResolver = context =>
    {
        // Prioritize Api-Key header over IP
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
        {
            return $"key:{apiKey}";
        }
        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";
    };
});

var app = builder.Build();

app.UseHttpRateLimiting();

app.MapGet("/api/data", () => Results.Ok(new { status = "Secure" }));

app.Run();
```

---

## 3. Frequently Asked Questions (FAQ)

### How does the library handle traffic behind reverse proxies or load balancers (NGINX, Cloudflare)?
`PartitionKeyResolver` defaults to inspecting `context.Connection.RemoteIpAddress`. When operating behind a reverse proxy, configure `ForwardedHeadersMiddleware` in ASP.NET Core (`app.UseForwardedHeaders()`) so that `RemoteIpAddress` resolves the true client IP reported in `X-Forwarded-For`.

### Can in-memory and Redis limiters be combined?
Yes. Through `RateLimiterPolicyBuilder`, you can configure an in-memory limiter for high-frequency public endpoints and distributed Redis limiters for mission-critical endpoints, or combine them using `CompositeRateLimiter`.

### Does the library cause Garbage Collection (GC) pauses?
No on the hot path. The acquisition return value is a `RateLimitLease` defined as a `readonly record struct`, eliminating heap allocations during permit acquisition and rejection.

### What happens if an endpoint has no rate limiting policy assigned?
It automatically inherits `DefaultLimiter` from `IRateLimiterPolicyRegistry` or the default `IRateLimiter` registered in DI. If neither is registered, the request passes through transparently.

---

## 4. Troubleshooting

### Symptom: All clients share the exact same limit quota
- **Cause**: `PartitionKeyResolver` resolves a static key (e.g. `"anonymous"` because all incoming requests appear from the internal load balancer IP `10.0.0.1`).
- **Resolution**: Ensure `app.UseForwardedHeaders()` is configured with `ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto`.

### Symptom: `InvalidOperationException: Rate limiting policy 'xyz' is not registered.`
- **Cause**: An endpoint declares `.RequireRateLimiting("xyz")` or `[EnableRateLimiting("xyz")]`, but policy `"xyz"` was never registered in `RateLimiterPolicyBuilder`.
- **Resolution**: Explicitly register `policies.Add*( "xyz", ...)` in `builder.Services.AddRateLimiting(...)`.

### Symptom: `Result.IsFailure` with error code `RateLimit.Redis.ConnectionFailed`
- **Cause**: Redis server is unavailable, the connection string is incorrect, or a network timeout occurred.
- **Resolution**: Verify Redis cluster network reachability. To keep serving traffic while Redis recovers, set `FailClosed = false` in `RateLimitingMiddlewareOptions`.

---

## 5. Migration Guide

### Migrating from `System.Threading.RateLimiting`
| `System.Threading.RateLimiting` | `EricksonLopez.RateLimiting` | Key Architectural Difference |
|---|---|---|
| `services.AddRateLimiter(...)` | `services.AddRateLimiting(...)` or `services.AddSlidingWindowRateLimiter(...)` | Optimized for ultra-low latency and Native AOT |
| `PartitionedRateLimiter.Create(...)` | `builder.AddSlidingWindow(...)` | Strongly typed builder without heavy lambdas |
| `app.UseRateLimiter()` | `app.UseHttpRateLimiting()` | Native telemetry and RFC header injection |
| `RateLimitLease.IsAcquired` | `RateLimitLease.IsAcquired` | Equivalent concept; zero-allocation struct instead of abstract base class |

### Migrating from `AspNetCoreRateLimit` (Legacy Library)
- Remove `IpRateLimitMiddleware` and cumbersome `appsettings.json` blocks.
- Replace with `builder.Services.AddFixedWindowRateLimiter(...)` and `app.UseHttpRateLimiting()`.
- Enjoy full compatibility with .NET 8/9/10 Native AOT without reflection overhead.

---

## 6. Performance Best Practices Guide

1. **Sliding Window vs Fixed Window**:
   - Use `FixedWindowRateLimiter` when boundary bursts are acceptable (minimal CPU overhead).
   - Use `SlidingWindowRateLimiter` (with `SegmentsPerWindow = 6` or `10`) when strict boundary burst protection is required.
2. **Tuning `MaxPartitions`**:
   - When serving millions of unique transient IPs, maintain `MaxPartitions` at a reasonable threshold (e.g. 20,000 to 50,000) to cap memory usage; the engine automatically prunes idle partitions.
3. **Avoid Redundant Redis Connection Multiplexers**:
   - If your application already registers `IConnectionMultiplexer`, use the overload `services.AddRedisRateLimiting(existingMultiplexer)` to share socket descriptors.
4. **Timely Concurrency Release**:
   - Always wrap acquisitions from `ConcurrencyRateLimiter` in `using var lease` blocks to prevent in-flight permit leaks.

---

## 7. Official Historic Changelog

### v1.0.0 (2026-09-22) — Official Initial Release
- **In-Memory Core:** Lock-free `FixedWindowRateLimiter`, `SlidingWindowRateLimiter`, `TokenBucketRateLimiter`, `ConcurrencyRateLimiter`, and transactional `CompositeRateLimiter` with automatic rollback.
- **ASP.NET Core:** High-performance middleware, `[EnableRateLimiting]`/`[DisableRateLimiting]` attributes, Minimal API endpoint convention builders, and standard `X-RateLimit-*` headers.
- **Distributed Redis:** `RedisSlidingWindowRateLimiter` (ZSET with GUID salt anti-collision) and atomic `RedisTokenBucketRateLimiter` over Redis Hashes.
- **Observabilidad:** Native `RateLimitingMetrics` for OpenTelemetry (`rate_limit.requests.total`, `rate_limit.lease.duration`).
- **Resilience & Hardening:** Configurable Fail-Open / Fail-Closed modes with `RateLimitingErrorCodes.ConnectionFailedCode`, anti-DoS partition bounding (`MaxPartitions`), integer overflow guards (CWE-190), and atomic `OneShotDisposer` (CWE-675).
- **Compatibility:** Full multi-targeting across `.NET 8.0`, `.NET 9.0`, and `.NET 10.0` with Native AOT verification and Strong-Naming.
