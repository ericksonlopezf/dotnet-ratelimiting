# Phase 3 · Progressive Showcase (Levels 0 through 10)

This guide structures the learning and demonstration of `EricksonLopez.RateLimiting` across 11 progressive levels, from theoretical fundamentals to distributed high-availability enterprise architecture.

---

## Level 0 · Conceptual Foundation

### What is the library?
`EricksonLopez.RateLimiting` is a suite of high-performance .NET libraries engineered for request throttling, perimeter API protection, and concurrency limiting in both standalone in-memory setups and distributed Redis environments.

### What problem does it solve?
It prevents resource exhaustion, denial-of-service (DoS) attacks, abusive consumption of expensive endpoints, and cascading outages caused by clients sending requests faster than downstream services can safely and equitably process.

### Why does it exist?
While .NET introduced `System.Threading.RateLimiting`, many production workloads encounter significant challenges:
1. Excessive heap allocations and garbage collection (GC) pauses under tens of thousands of concurrent requests.
2. Difficulty coordinating distributed quotas atomically and in an AOT-friendly manner across Kubernetes pods without heavy external dependencies.
3. Lack of first-class concurrency limiting with automatic rollback in complex composite policies.
4. Absence of turnkey integration with resilient operational patterns (Fail-Open vs Fail-Closed).

`EricksonLopez.RateLimiting` solves these issues with zero unnecessary heap allocations, full Native AOT compatibility (no runtime reflection or slow serializers), and atomic Redis Lua scripts.

### Key Advantages
- **100% Native AOT & Trimming-Safe**: Built from day zero for minimal container footprints and instant startup in .NET 8, 9, and 10.
- **Ultra-Low Latency**: Lock-free in-memory hot paths; thread-safe partitions with deterministic $O(1)$ access.
- **Distributed Atomicity**: Lua scripts in Redis using Sorted Sets (ZSET) and Hashes that eliminate race conditions across multi-instance clusters.
- **Operational Resilience**: Fail-Open modes to guarantee business availability or Fail-Closed for strict security, paired with structured programmatic callbacks.
- **Web Standards**: Standard `X-RateLimit-*` and `Retry-After` response headers aligned with IETF standards and support for RFC 7807 (ProblemDetails).

### Trade-offs & Considerations
- In in-memory mode, each node maintains its own local counter, meaning traffic must be load-balanced evenly or session affinity used if Redis is not configured.
- In distributed Redis mode with sliding window ZSETs, environments with very long time windows and millions of permits may consume more Redis memory compared to coarse approximate counters.

### Comparison with Alternatives
| Feature | `EricksonLopez.RateLimiting` | `System.Threading.RateLimiting` | `AspNetCoreRateLimit` |
|---|---|---|---|
| **Native AOT Compatible** | ✅ 100% Warning-Free | ⚠️ Partial | ❌ No (Heavy Reflection) |
| **Zero-Allocation Memory Path** | ✅ Optimized (`record struct`) | ⚠️ Allocates `Lease` objects | ❌ Multiple allocations |
| **Distributed Redis (Atomic Lua)** | ✅ Native ZSET & Hash | ❌ Not included (needs 3rd party) | ⚠️ Non-atomic scripts |
| **Fail-Open / Fail-Closed Hook** | ✅ Yes (`OnRedisFailure`) | ❌ No | ❌ No |
| **Composite Limiter with Rollback** | ✅ Yes (`CompositeRateLimiter`) | ❌ No | ❌ No |
| **OpenTelemetry Metrics** | ✅ Native (`Meter`, `Histogram`) | ⚠️ Limited | ❌ No |

---

## Level 1 · Quick Start

### Installation
```bash
# In-memory Core
dotnet add package EricksonLopez.RateLimiting

# ASP.NET Core Integration
dotnet add package EricksonLopez.RateLimiting.AspNetCore
```

### Minimal Setup & Dependency Injection
```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

// 1. Register in-memory sliding window rate limiter
builder.Services.AddSlidingWindowRateLimiter(options =>
{
    options.PermitLimit = 100;
    options.Window = TimeSpan.FromMinutes(1);
});

// 2. Register HTTP rate limiting middleware
builder.Services.AddHttpRateLimiting();

var app = builder.Build();

// 3. Enable middleware in request pipeline
app.UseHttpRateLimiting();

app.MapGet("/api/greeting", () => "Hello World!");

app.Run();
```

---

## Level 2 · Comprehensive Configuration

Demonstrates configuration options across `RateLimiterOptions`, `ConcurrencyRateLimiterOptions`, and `RateLimitingMiddlewareOptions`:

```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// Comprehensive rate limiter configuration
builder.Services.AddSlidingWindowRateLimiter(options =>
{
    options.PermitLimit = 500;                            // 500 requests
    options.Window = TimeSpan.FromMinutes(5);             // across a 5-minute window
    options.SegmentsPerWindow = 10;                       // 10 segments of 30s each
    options.MaxPartitions = 25_000;                       // Maximum 25,000 active partitions
});

// Comprehensive middleware configuration
builder.Services.AddHttpRateLimiting(options =>
{
    options.PermitCost = 1;                               // Base cost per request
    options.FailClosed = false;                           // Fail-Open on infrastructure failures
    options.PartitionKeyResolver = context =>
    {
        // Custom resolution by Api-Key header or remote client IP
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var key) && !string.IsNullOrWhiteSpace(key))
        {
            return $"apikey:{key}";
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    };
});
```

---

## Level 3 · Real-World Partitioning Scenarios

Common perimeter partitioning patterns:

### 1. Throttling by Client IP
```csharp
options.PartitionKeyResolver = context =>
    context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
```

### 2. Throttling by Authenticated User (Claims)
```csharp
options.PartitionKeyResolver = context =>
{
    var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    return !string.IsNullOrEmpty(userId) ? $"user:{userId}" : $"ip:{context.Connection.RemoteIpAddress}";
};
```

### 3. Multi-Tenant SaaS Throttling
```csharp
options.PartitionKeyResolver = context =>
{
    if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var tenant) && !string.IsNullOrWhiteSpace(tenant))
    {
        return $"tenant:{tenant}";
    }
    return "tenant:default";
};
```

---

## Level 4 · Advanced Policy Mapping

Configuring named policies using `RateLimiterPolicyBuilder` and attaching them via `RequireRateLimiting` and `DisableRateLimiting`:

```csharp
builder.Services.AddRateLimiting(policies =>
{
    // Anonymous user tier (Fixed Window)
    policies.AddFixedWindow("tier-anonymous", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
    });

    // Subscribed user tier (Smooth Sliding Window)
    policies.AddSlidingWindow("tier-pro", o =>
    {
        o.PermitLimit = 100;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
    });

    // High-burst and bulk download tier (Token Bucket)
    policies.AddTokenBucket("tier-burst", o =>
    {
        o.PermitLimit = 200;
        o.Window = TimeSpan.FromSeconds(10);
    });

    // Fallback default policy when endpoint specifies no metadata
    policies.SetDefaultPolicy("tier-anonymous");
});

var app = builder.Build();
app.UseHttpRateLimiting();

// Endpoint protected with named policy
app.MapGet("/api/pro/data", () => Results.Ok("Pro Data"))
   .RequireRateLimiting("tier-pro");

// Exempt endpoint (Health checks / metrics)
app.MapGet("/health", () => Results.Ok("Healthy"))
   .DisableRateLimiting();

// Endpoint inheriting default policy ("tier-anonymous")
app.MapGet("/api/public", () => Results.Ok("Public content"));
```

---

## Level 5 · Concurrency Limiting

Protecting compute-intensive or finite system resources (e.g. PDF generation, ML inference, Excel exports) using `ConcurrencyRateLimiter`:

```csharp
using EricksonLopez.RateLimiting;

var concurrencyLimiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
{
    PermitLimit = 3,         // Maximum 3 concurrent operations per partition key
    MaxPartitions = 5_000
});

// Acquire permit
var leaseResult = await concurrencyLimiter.AcquireAsync("tenant-report-generator", permits: 1);

if (leaseResult.IsSuccess && leaseResult.Value.IsAcquired)
{
    // The using statement guarantees slot release upon disposal
    using var lease = leaseResult.Value;
    
    // Execute heavy workload safely
    await GenerateHeavyReportAsync();
}
else
{
    Console.WriteLine("Server busy: please retry shortly.");
}
```

---

## Level 6 · Error Handling & Resilience

Comprehensive retry handling, RFC 7807 ProblemDetails serialization, and graceful degradation during Redis outages:

```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;

builder.Services.AddHttpRateLimiting(options =>
{
    // Fail-Open to ensure business availability
    options.FailClosed = false;

    // Custom rejection handler (HTTP 429) emitting RFC 7807 ProblemDetails
    options.OnRejected = async (context, lease, ct) =>
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/problem+json";
        
        var retryAfterSec = lease.RetryAfter?.TotalSeconds ?? 1;
        var problem = new
        {
            type = "https://tools.ietf.org/html/rfc6585#section-4",
            title = "Too Many Requests",
            status = 429,
            detail = $"Request quota exceeded. Retry available in {retryAfterSec} seconds.",
            instance = context.Request.Path.ToString()
        };

        await context.Response.WriteAsJsonAsync(problem, ct);
    };

    // Resilience callback on Redis infrastructure failure
    options.OnRedisFailure = async (context, error, ct) =>
    {
        // Safe pattern-matching using public error codes
        if (error.Code == RateLimitingErrorCodes.ConnectionFailedCode)
        {
            context.Response.Headers["X-RateLimit-Degraded"] = "true";
            // Record alert in monitoring telemetry...
        }
        await Task.CompletedTask;
    };
});
```

---

## Level 7 · Scalability & Telemetry

Continuous telemetry monitoring using native OpenTelemetry metrics:

```csharp
using System.Diagnostics.Metrics;
using EricksonLopez.RateLimiting;

// Inspect official library meter
var meterName = RateLimitingMetrics.MeterName;       // "EricksonLopez.RateLimiting"
var meterVersion = RateLimitingMetrics.MeterVersion; // "1.0.0"

// Record custom pipeline telemetry
RateLimitingMetrics.RecordRequest(
    limiterType: "sliding_window",
    status: "acquired",
    durationMs: 0.12);

// Configure with OpenTelemetry .NET SDK:
// builder.Services.AddOpenTelemetry()
//    .WithMetrics(metrics => metrics.AddMeter(RateLimitingMetrics.MeterName));
```

---

## Level 8 · Customization & Extensibility

Custom `IRateLimiter` implementation for specialized domain requirements (e.g. dynamic external quotas or legacy system synchronization):

```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.Result;

public sealed class CustomDynamicRateLimiter : IRateLimiter
{
    public Task<Result<RateLimitLease>> AcquireAsync(
        string key, 
        int permits = 1, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Custom domain evaluation (e.g. database lookup or custom algorithm)
        bool allowed = DateTime.UtcNow.Second % 2 == 0;

        if (allowed)
        {
            var lease = RateLimitLease.Successful(remainingPermits: 10, resetTime: DateTimeOffset.UtcNow.AddMinutes(1));
            return Task.FromResult(Result<RateLimitLease>.Success(lease));
        }
        else
        {
            var lease = RateLimitLease.Rejected(retryAfter: TimeSpan.FromSeconds(5), limit: 10);
            return Task.FromResult(Result<RateLimitLease>.Success(lease));
        }
    }
}
```

---

## Level 9 · Distributed Architecture (Redis)

Horizontal scalability across multi-pod Kubernetes clusters using Redis:

```csharp
using EricksonLopez.RateLimiting.Redis;

// 1. Distributed Sliding Window (via atomic Lua script over Redis Sorted Sets)
builder.Services.AddRedisRateLimiting(options =>
{
    options.Configuration = "redis-cluster.internal:6379,abortConnect=false";
    options.KeyPrefix = "prod:rl:";
    options.MaxPermits = 1000;
    options.WindowDuration = TimeSpan.FromMinutes(1);
    options.Database = 0;
});

// 2. Optional: Distributed Token Bucket (via atomic Lua script over Redis Hashes)
// builder.Services.AddRedisTokenBucketRateLimiting(options =>
// {
//     options.Configuration = "redis-cluster.internal:6379,abortConnect=false";
//     options.KeyPrefix = "prod:tb:";
//     options.TokenLimit = 500;
//     options.TokensPerPeriod = 50;
//     options.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
// });
```

---

## Level 10 · Multi-Layer Composite Enterprise Architecture

Layering multiple rate limits using boolean AND logic with transactional lease rollback:
- Burst limit: Maximum 20 requests per second (Token Bucket).
- Sustained limit: Maximum 300 requests per minute (Sliding Window).
- In-flight concurrency limit: Maximum 5 concurrent operations (Concurrency).

```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;

var tbBurst = new TokenBucketRateLimiter(new RateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromSeconds(1) });
var swSustained = new SlidingWindowRateLimiter(new RateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) });
var ccInFlight = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions { PermitLimit = 5 });

// Enterprise composite rate limiter with automatic rollback on rejection
var enterpriseLimiter = new CompositeRateLimiter(tbBurst, swSustained, ccInFlight);

// Register in policy builder
builder.Services.AddRateLimiting(policies =>
{
    policies.AddPolicy("enterprise-tier", enterpriseLimiter);
    policies.SetDefaultPolicy("enterprise-tier");
});
```
