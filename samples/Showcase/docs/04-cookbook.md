# Phase 4 · Official Production Cookbook

This cookbook compiles practical, production-ready patterns derived strictly from the public API surface of `EricksonLopez.RateLimiting`.

---

## Recipe 1: Perimeter Protection Against Credential Stuffing & Brute Force

### Problem
A critical authentication endpoint (`/api/auth/login`) faces credential stuffing attacks and brute force attempts from rotating IP addresses.

### Solution
Apply a discrete fixed window policy (`FixedWindowRateLimiter`) allowing 5 attempts per minute partitioned by remote client IP, emitting standard `Retry-After` headers.

### Complete Code
```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRateLimiting(policies =>
{
    policies.AddFixedWindow("login-protection", options =>
    {
        options.PermitLimit = 5;
        options.Window = TimeSpan.FromMinutes(1);
    });
}, middlewareOptions =>
{
    middlewareOptions.PartitionKeyResolver = context =>
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
});

var app = builder.Build();
app.UseHttpRateLimiting();

app.MapPost("/api/auth/login", () => Results.Ok(new { status = "Authenticated" }))
   .RequireRateLimiting("login-protection");

app.Run();
```

### Explanation
The `"login-protection"` policy isolates request counts to each remote IP address using `PartitionKeyResolver`. Upon the 6th attempt within the 1-minute window, the middleware intercepts the request prior to invoking the login handler, responding with HTTP 429 Too Many Requests and injecting the `Retry-After` header.

### Best Practices
- Use concise windows (1 to 5 minutes) for authentication endpoints.
- When operating behind reverse proxies (Cloudflare, NGINX, ALB), ensure `ForwardedHeadersMiddleware` is configured so that `RemoteIpAddress` reflects the real client IP.

### Common Pitfalls
- Forgetting to register `app.UseHttpRateLimiting()` before mapping endpoints, resulting in unintercepted traffic.

---

## Recipe 2: High Availability with Fail-Open and Graceful Degradation

### Problem
In distributed environments, the Redis cluster may undergo failovers, restarts, or network partitioning. The application must not crash or reject all legitimate users, avoiding catastrophic business downtime.

### Solution
Configure `FailClosed = false` (Fail-Open) and register an `OnRedisFailure` callback to alert monitoring systems and mark HTTP responses with diagnostic headers (`X-RateLimit-Degraded: true`).

### Complete Code
```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;
using EricksonLopez.RateLimiting.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRedisRateLimiting(options =>
{
    options.Configuration = "redis.prod.internal:6379,abortConnect=false";
    options.MaxPermits = 100;
    options.WindowDuration = TimeSpan.FromMinutes(1);
});

builder.Services.AddHttpRateLimiting(options =>
{
    options.FailClosed = false; // Allow traffic through when Redis fails

    options.OnRedisFailure = async (context, error, ct) =>
    {
        if (error.Code == RateLimitingErrorCodes.ConnectionFailedCode)
        {
            context.Response.Headers["X-RateLimit-Degraded"] = "true";
        }
        await Task.CompletedTask;
    };
});

var app = builder.Build();
app.UseHttpRateLimiting();
app.MapGet("/api/catalog", () => Results.Ok(new { items = new[] { "Item1", "Item2" } }));
app.Run();
```

### Explanation
When Redis encounters socket errors or timeouts, the limiter emits `Result<RateLimitLease>.Failure`. Observing `FailClosed = false`, the middleware invokes `OnRedisFailure` and passes the request downstream to `_next(context)`.

### Best Practices
- Always check the official public constant `RateLimitingErrorCodes.ConnectionFailedCode` rather than matching arbitrary strings.
- Connect observability dashboards to `X-RateLimit-Degraded` to alert SRE teams of Redis connectivity loss.

### Common Pitfalls
- Enabling `FailClosed = true` in mission-critical e-commerce APIs where transient Redis downtime would block transactions.

---

## Recipe 3: Hierarchical Partitioning for Multi-Tenant SaaS

### Problem
A SaaS platform serves enterprise tenants as well as individual users. The system must throttle by corporate tenant when present, falling back to authenticated user ID, or remote IP address.

### Solution
Customize `PartitionKeyResolver` implementing a hierarchical cascade: Tenant ➔ User ➔ IP.

### Complete Code
```csharp
using System.Security.Claims;
using EricksonLopez.RateLimiting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpRateLimiting(options =>
{
    options.PartitionKeyResolver = context =>
    {
        // 1. Corporate Tenant Header
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenant) && !string.IsNullOrWhiteSpace(tenant))
        {
            return $"tenant:{tenant}";
        }

        // 2. Authenticated User Identity
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}";
        }

        // 3. Fallback to Client IP
        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";
    };
});
```

### Explanation
The hierarchy ensures corporate tenants share their aggregate quota across employees without one organization's usage impacting another.

### Best Practices
- Use consistent namespace prefixes (`tenant:`, `user:`, `ip:`) to prevent partition key collisions across scopes.

---

## Recipe 4: Concurrency Limiting for Heavy Compute Workloads

### Problem
A PDF export or reporting endpoint exhausts system RAM if more than 3 requests execute concurrently.

### Solution
Use `ConcurrencyRateLimiter` with deterministic resource reclamation via `using var lease`.

### Complete Code
```csharp
using EricksonLopez.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

var pdfLimiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
{
    PermitLimit = 3
});

var app = builder.Build();

app.MapPost("/api/reports/generate-pdf", async (HttpContext context) =>
{
    var key = context.Connection.RemoteIpAddress?.ToString() ?? "global";
    var result = await pdfLimiter.AcquireAsync(key, permits: 1);

    if (result.IsFailure || !result.Value.IsAcquired)
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    using (result.Value)
    {
        // Simulate compute-heavy processing
        await Task.Delay(2000);
        return Results.Ok(new { message = "Report generated successfully" });
    }
});

app.Run();
```

### Explanation
`ConcurrencyRateLimiter` atomically decrements the slot count upon `Dispose()`. Wrapping the acquired lease in a `using` block ensures slots are returned even if unhandled exceptions occur during execution.

### Best Practices
- Dispose of leases as early as possible.
- Set reasonable request timeouts to prevent hung requests from monopolizing concurrency slots indefinitely.

---

## Recipe 5: Multi-Tier Composite Policies (Burst + Sustained)

### Problem
Allow bursts of up to 10 requests per second for snappy UI interactions, while capping sustained volume at 100 requests per minute.

### Solution
Compose `TokenBucketRateLimiter` and `SlidingWindowRateLimiter` inside a `CompositeRateLimiter`.

### Complete Code
```csharp
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.Policies;

var burstLimiter = new TokenBucketRateLimiter(new RateLimiterOptions
{
    PermitLimit = 10,
    Window = TimeSpan.FromSeconds(1)
});

var sustainedLimiter = new SlidingWindowRateLimiter(new RateLimiterOptions
{
    PermitLimit = 100,
    Window = TimeSpan.FromMinutes(1)
});

var composite = new CompositeRateLimiter(burstLimiter, sustainedLimiter);

var registry = new RateLimiterPolicyRegistry();
registry.Register("dual-throttle", composite);
```

### Explanation
`CompositeRateLimiter` checks each limiter sequentially with AND semantics. If the sustained limiter rejects a request, all previously acquired leases from the burst limiter are automatically rolled back, preventing phantom quota consumption.
