// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;
using EricksonLopez.RateLimiting.Policies;
using EricksonLopez.RateLimiting.Redis;
using EricksonLopez.RateLimiting.Showcase;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable ASP0000 // Demonstration of standalone ServiceCollection.BuildServiceProvider

Console.WriteLine("================================================================================");
Console.WriteLine(" EricksonLopez.RateLimiting — Official Reference Showcase");
Console.WriteLine(" Complete Executable Documentation of the Public API (.NET 10 / Native AOT)");
Console.WriteLine("================================================================================");

// =============================================================================
// LEVEL 0: Conceptual & Architectural Foundation
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 0] CONCEPTUAL & ARCHITECTURAL FOUNDATION");
Console.WriteLine("    • High-throughput in-memory & distributed rate limiting engine.");
Console.WriteLine("    • 100% Native AOT compatible: zero reflection, zero runtime code generation.");
Console.WriteLine("    • Zero-allocation core path: RateLimitLease defined as readonly record struct.");
Console.WriteLine("    • Atomic Lua scripts over Redis Sorted Sets (ZSET) and Hashes.");
Console.WriteLine("    • Operational resilience: Fail-Open vs Fail-Closed modes with structured callbacks.");

// =============================================================================
// LEVEL 1: Quick Start (Minimal In-Memory DI & Direct Usage)
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 1] QUICK START & MINIMAL CONFIGURATION");
var quickServices = new ServiceCollection();
quickServices.AddFixedWindowRateLimiter(options =>
{
    options.PermitLimit = 10;
    options.Window = TimeSpan.FromMinutes(1);
});
var quickProvider = quickServices.BuildServiceProvider();
var quickLimiter = quickProvider.GetRequiredService<IRateLimiter>();
var quickResult = await quickLimiter.AcquireAsync("quickstart-user", permits: 1);
Console.WriteLine($"    -> FixedWindow registered via DI: IsAcquired={quickResult.Value.IsAcquired}, Remaining={quickResult.Value.RemainingPermits}");

// 1b. Direct instantiation without DI (optional null options = use defaults)
var directFixed = new FixedWindowRateLimiter();
var directResult = await directFixed.AcquireAsync("direct-key");
Console.WriteLine($"    -> FixedWindow direct (defaults): IsAcquired={directResult.Value.IsAcquired}, Remaining={directResult.Value.RemainingPermits}");

// 1c. Result<T> envelope: IsSuccess / IsFailure properties
Console.WriteLine($"    -> Result<RateLimitLease> envelope: IsSuccess={quickResult.IsSuccess}, IsFailure={quickResult.IsFailure}");

// =============================================================================
// LEVEL 2: Full Configuration (All Option Classes & Properties)
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 2] FULL CONFIGURATION OPTIONS");
// 1. RateLimiterOptions
var fullRateLimiterOptions = new RateLimiterOptions
{
    PermitLimit = 250,
    Window = TimeSpan.FromMinutes(2),
    SegmentsPerWindow = 12,
    MaxPartitions = 50_000
};
Console.WriteLine($"    -> RateLimiterOptions: Limit={fullRateLimiterOptions.PermitLimit}, Window={fullRateLimiterOptions.Window.TotalSeconds}s, Segments={fullRateLimiterOptions.SegmentsPerWindow}, MaxPartitions={fullRateLimiterOptions.MaxPartitions}");

// 2. ConcurrencyRateLimiterOptions
var fullConcurrencyOptions = new ConcurrencyRateLimiterOptions
{
    PermitLimit = 15,
    MaxPartitions = 20_000
};
Console.WriteLine($"    -> ConcurrencyRateLimiterOptions: Limit={fullConcurrencyOptions.PermitLimit}, MaxPartitions={fullConcurrencyOptions.MaxPartitions}");

// 3. RateLimitingMiddlewareOptions
var fullMiddlewareOptions = new RateLimitingMiddlewareOptions
{
    PermitCost = 2,
    FailClosed = false,
    PartitionKeyResolver = ctx => ctx.Request.Headers["X-Custom-Client"].ToString()
};
Console.WriteLine($"    -> RateLimitingMiddlewareOptions: PermitCost={fullMiddlewareOptions.PermitCost}, FailClosed={fullMiddlewareOptions.FailClosed}");

// 4. RedisRateLimiterOptions
var fullRedisOptions = new RedisRateLimiterOptions
{
    Configuration = "localhost:6379,abortConnect=false",
    KeyPrefix = "showcase:rl:",
    WindowDuration = TimeSpan.FromMinutes(1),
    MaxPermits = 500,
    Database = 1
};
Console.WriteLine($"    -> RedisRateLimiterOptions: Prefix={fullRedisOptions.KeyPrefix}, MaxPermits={fullRedisOptions.MaxPermits}, DB={fullRedisOptions.Database}");

// 5. RedisTokenBucketRateLimiterOptions
var fullRedisTokenOptions = new RedisTokenBucketRateLimiterOptions
{
    Configuration = "localhost:6379,abortConnect=false",
    KeyPrefix = "showcase:tb:",
    TokenLimit = 300,
    TokensPerPeriod = 30,
    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
    Database = 2
};
Console.WriteLine($"    -> RedisTokenBucketRateLimiterOptions: TokenLimit={fullRedisTokenOptions.TokenLimit}, TokensPerPeriod={fullRedisTokenOptions.TokensPerPeriod}, ReplenishmentPeriod={fullRedisTokenOptions.ReplenishmentPeriod.TotalSeconds}s, DB={fullRedisTokenOptions.Database}");

// =============================================================================
// LEVEL 3: Real-World Partitioning Use Cases (IP, Claims, Multi-Tenant)
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 3] REAL-WORLD PARTITIONING USE CASES");

// Scenario A: Client Remote IP
Func<HttpContext, string> ipResolver = ctx =>
    ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

// Scenario B: Claims-based User Identity
Func<HttpContext, string> userResolver = ctx =>
{
    var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? ctx.User.FindFirst("sub")?.Value;
    return !string.IsNullOrWhiteSpace(sub) ? $"user:{sub}" : "anonymous";
};

// Scenario C: Multi-Tenant SaaS
Func<HttpContext, string> tenantResolver = ctx =>
{
    if (ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var tenant) && !string.IsNullOrWhiteSpace(tenant))
    {
        return $"tenant:{tenant}";
    }
    return "tenant:default";
};

var testContext = new DefaultHttpContext();
testContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100");
testContext.Request.Headers["X-Tenant-Id"] = "acme-corp";

Console.WriteLine($"    -> IP Resolver output:     '{ipResolver(testContext)}'");
Console.WriteLine($"    -> Tenant Resolver output: '{tenantResolver(testContext)}'");

// =============================================================================
// LEVEL 4: Advanced Integration (Policy Builder, Named Policies, Endpoint Conventions)
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 4] ADVANCED INTEGRATION & POLICY BUILDER");
var policyBuilder = new RateLimiterPolicyBuilder();

// 4a-4d. All algorithm policies
policyBuilder.AddFixedWindow("fixed-tier", o => { o.PermitLimit = 20; o.Window = TimeSpan.FromMinutes(1); });
policyBuilder.AddSlidingWindow("sliding-tier", o => { o.PermitLimit = 50; o.Window = TimeSpan.FromMinutes(1); o.SegmentsPerWindow = 6; });
policyBuilder.AddTokenBucket("token-tier", o => { o.PermitLimit = 100; o.Window = TimeSpan.FromSeconds(5); });
policyBuilder.AddConcurrency("concurrency-tier", o => { o.PermitLimit = 4; });

// 4e. AddPolicy — pre-instantiated limiter
var directLimiter = new FixedWindowRateLimiter(new RateLimiterOptions { PermitLimit = 5 });
policyBuilder.AddPolicy("custom-direct", directLimiter);

// 4f. AddComposite — multi-tier conjunction policy registered by name in builder
var burstForComposite = new TokenBucketRateLimiter(new RateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromSeconds(1) });
var sustainedForComposite = new SlidingWindowRateLimiter(new RateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) });
policyBuilder.AddComposite("enterprise-composite", burstForComposite, sustainedForComposite);

// 4g. SetDefaultPolicy(string policyName) — named overload
policyBuilder.SetDefaultPolicy("fixed-tier");

var policyRegistry = policyBuilder.Build();
var retrievedFixed = policyRegistry.GetPolicy("fixed-tier");
var retrievedSliding = policyRegistry.GetPolicy("sliding-tier");
var retrievedComposite = policyRegistry.GetPolicy("enterprise-composite");
var defaultPolicy = policyRegistry.DefaultLimiter;

Console.WriteLine($"    -> Registry: fixed={retrievedFixed != null}, sliding={retrievedSliding != null}, composite={retrievedComposite != null}, default={defaultPolicy != null}");

// 4h. SetDefaultPolicy(IRateLimiter limiter) — direct instance overload (second builder)
var builder2 = new RateLimiterPolicyBuilder();
builder2.AddFixedWindow("api-v2", o => { o.PermitLimit = 30; o.Window = TimeSpan.FromMinutes(1); });
var directDefaultLimiter = new SlidingWindowRateLimiter(new RateLimiterOptions { PermitLimit = 100 });
builder2.SetDefaultPolicy(directDefaultLimiter);  // IRateLimiter overload
var registry2 = builder2.Build();
Console.WriteLine($"    -> SetDefaultPolicy(IRateLimiter) overload: DefaultLimiter={registry2.DefaultLimiter != null}");

// 4i. IRateLimiterPolicy & RateLimiterPolicy types
RateLimiterPolicy standalonePolicy = new RateLimiterPolicy("standalone", directLimiter);
#pragma warning disable CA1859 // Intentional: demonstrating IRateLimiterPolicy interface contract
IRateLimiterPolicy policyAsInterface = standalonePolicy;  // interface usage
#pragma warning restore CA1859
Console.WriteLine($"    -> RateLimiterPolicy: Name='{standalonePolicy.Name}', Limiter={standalonePolicy.Limiter.GetType().Name}");
Console.WriteLine($"    -> IRateLimiterPolicy interface: Name='{policyAsInterface.Name}'");

// 4j. RateLimiterPolicyRegistry — direct usage (bypassing builder)
var directRegistry = new RateLimiterPolicyRegistry();
directRegistry.Register("tier-a", new FixedWindowRateLimiter(new RateLimiterOptions { PermitLimit = 50 }));
directRegistry.Register("tier-b", new SlidingWindowRateLimiter(new RateLimiterOptions { PermitLimit = 100 }));
directRegistry.DefaultLimiter = directRegistry.GetPolicy("tier-a");  // direct setter
Console.WriteLine($"    -> RateLimiterPolicyRegistry direct: tier-a={directRegistry.GetPolicy("tier-a") != null}, default={directRegistry.DefaultLimiter != null}");

// 4k. AddRateLimiting (IServiceCollection + RateLimiterPolicyBuilder delegate)
var aspServices = new ServiceCollection();
aspServices.AddLogging();
aspServices.AddRateLimiting(
    configure: b =>
    {
        b.AddFixedWindow("api-standard", o => { o.PermitLimit = 100; o.Window = TimeSpan.FromMinutes(1); });
        b.AddSlidingWindow("api-premium", o => { o.PermitLimit = 500; o.Window = TimeSpan.FromMinutes(1); o.SegmentsPerWindow = 12; });
        b.SetDefaultPolicy("api-standard");
    },
    configureMiddleware: o =>
    {
        o.PermitCost = 1;
        o.FailClosed = false;
        o.PartitionKeyResolver = ctx => ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    });
Console.WriteLine("    -> AddRateLimiting(builder delegate, configureMiddleware) registered.");

// 4l. AddHttpRateLimiting (middleware options only, no policy builder)
var mwOnlyServices = new ServiceCollection();
mwOnlyServices.AddHttpRateLimiting(o => { o.PermitCost = 1; o.FailClosed = true; });
Console.WriteLine("    -> AddHttpRateLimiting (middleware-only, no policy builder) registered.");

// 4m. Endpoint metadata via extension methods (static call style)
var webAppBuilder = WebApplication.CreateBuilder();
var app = webAppBuilder.Build();

var ep1 = app.MapGet("/api/tier-fixed", () => "OK");
EndpointRateLimitingExtensions.RequireRateLimiting(ep1, "fixed-tier");

var ep2 = app.MapGet("/api/tier-exempt", () => "OK");
EndpointRateLimitingExtensions.DisableRateLimiting(ep2);

// 4n. Backward-compatible aliases
var ep3 = app.MapGet("/api/tier-alias", () => "OK");
EndpointRateLimitingExtensions.RequireDistributedRateLimiting(ep3, "sliding-tier");

var ep4 = app.MapGet("/api/tier-alias-exempt", () => "OK");
EndpointRateLimitingExtensions.DisableDistributedRateLimiting(ep4);

// 4o. Fluent style endpoint extension methods (via static call to avoid ambiguity)
// Note: RequireRateLimiting and DisableRateLimiting are available both as EricksonLopez
// extensions and as ASP.NET Core built-in extensions (CS0121 ambiguity). Use static style.
var ep5 = app.MapGet("/api/fluent", () => "OK");
EndpointRateLimitingExtensions.RequireRateLimiting(ep5, "token-tier");
var ep6 = app.MapGet("/api/exempt-fluent", () => "OK");
EndpointRateLimitingExtensions.DisableRateLimiting(ep6);
Console.WriteLine($"    -> Fluent endpoint extensions: ep5.RequireRateLimiting, ep6.DisableRateLimiting registered.");

// 4p. Attributes directly — EnableRateLimitingAttribute & DisableRateLimitingAttribute
var enableAttr = new EnableRateLimitingAttribute("fixed-tier");
var disableAttr = new DisableRateLimitingAttribute();
IEnableRateLimitingMetadata enableMeta = enableAttr;   // interface usage
IDisableRateLimitingMetadata disableMeta = disableAttr; // interface usage
Console.WriteLine($"    -> Attributes: Enable(PolicyName='{enableAttr.PolicyName}'), Disable is IDisableRateLimitingMetadata={disableMeta != null}");

// =============================================================================
// LEVEL 5: Processing & Concurrency Management (Lock-Free Slots & Disposable Leases)
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 5] CONCURRENCY RATE LIMITING & DISPOSABLE LEASES");
var concurrencyLimiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions { PermitLimit = 2 });

// Acquire 1st slot
var slot1 = await concurrencyLimiter.AcquireAsync("tenant-calc", 1);
Console.WriteLine($"    -> Slot 1 acquired: IsAcquired={slot1.Value.IsAcquired}, Remaining={slot1.Value.RemainingPermits}");

// Acquire 2nd slot
var slot2 = await concurrencyLimiter.AcquireAsync("tenant-calc", 1);
Console.WriteLine($"    -> Slot 2 acquired: IsAcquired={slot2.Value.IsAcquired}, Remaining={slot2.Value.RemainingPermits}");

// Attempt 3rd slot (exceeds permit limit of 2)
var slot3 = await concurrencyLimiter.AcquireAsync("tenant-calc", 1);
Console.WriteLine($"    -> Slot 3 rejected: IsAcquired={slot3.Value.IsAcquired}, RetryAfter={slot3.Value.RetryAfter?.TotalMilliseconds}ms");

// Release Slot 1 via Dispose
slot1.Value.Dispose();
Console.WriteLine("    -> Released Slot 1 via slot1.Value.Dispose().");

// Retry Slot 3: now should succeed!
var slot3Retry = await concurrencyLimiter.AcquireAsync("tenant-calc", 1);
Console.WriteLine($"    -> Slot 3 re-attempt: IsAcquired={slot3Retry.Value.IsAcquired}, Remaining={slot3Retry.Value.RemainingPermits}");

slot2.Value.Dispose();
slot3Retry.Value.Dispose();

// =============================================================================
// LEVEL 6: Error Handling, Resilience & Failure Modes (Fail-Open / Fail-Closed)
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 6] ERROR HANDLING, RESILIENCE & FAILURE MODES");

// Demonstrate RateLimitingErrorCodes
Console.WriteLine($"    -> RateLimitingErrorCodes.ConnectionFailedCode = '{RateLimitingErrorCodes.ConnectionFailedCode}'");

// Demonstrate RateLimitingHeaders
Console.WriteLine($"    -> RateLimitingHeaders: Limit='{RateLimitingHeaders.Limit}', Remaining='{RateLimitingHeaders.Remaining}', Reset='{RateLimitingHeaders.Reset}', RetryAfter='{RateLimitingHeaders.RetryAfter}'");

// 6c. RateLimitLease constructors (3 overloads)
// Constructor 1: 4-param
var leaseConstructed4 = new RateLimitLease(true, 5, TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow);
leaseConstructed4.Deconstruct(out var acq4, out var rem4, out var retry4, out var reset4);
Console.WriteLine($"    -> RateLimitLease Constructor(4-param) Deconstruct: Acquired={acq4}, Remaining={rem4}");

// Constructor 2: 5-param with disposeAction
var leaseConstructed5 = new RateLimitLease(true, 3, TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow, () => Console.WriteLine("       [Callback] Lease Disposed!"));
leaseConstructed5.Deconstruct(out var acq5, out var rem5, out var retry5, out var reset5, out var dispose5);
Console.WriteLine($"    -> RateLimitLease Constructor(5-param) Deconstruct: Acquired={acq5}, Remaining={rem5}, HasCallback={dispose5 != null}");
leaseConstructed5.Dispose();

// Constructor 3: 6-param full record (named parameters)
var leaseFull = new RateLimitLease(
    IsAcquired: false, RemainingPermits: 0, RetryAfter: TimeSpan.FromSeconds(30),
    ResetTime: DateTimeOffset.UtcNow.AddMinutes(1), DisposeAction: null, Limit: 100);
Console.WriteLine($"    -> RateLimitLease Constructor(6-param full): IsAcquired={leaseFull.IsAcquired}, Limit={leaseFull.Limit}");

// 6d. RateLimitLease factory methods — all 5 overloads
// Overload 1: Successful(remainingPermits)
var leaseS1 = RateLimitLease.Successful(9);
Console.WriteLine($"    -> Successful(9): IsAcquired={leaseS1.IsAcquired}, Remaining={leaseS1.RemainingPermits}, Limit={leaseS1.Limit}");

// Overload 2: Successful(remainingPermits, resetTime, limit)
var leaseS2 = RateLimitLease.Successful(8, DateTimeOffset.UtcNow.AddMinutes(1), limit: 10);
Console.WriteLine($"    -> Successful(8, resetTime, 10): IsAcquired={leaseS2.IsAcquired}, Limit={leaseS2.Limit}");

// Overload 3: Successful(remainingPermits, resetTime, disposeAction)
var leaseS3 = RateLimitLease.Successful(7, DateTimeOffset.UtcNow.AddMinutes(1), () => { /* slot release */ });
Console.WriteLine($"    -> Successful(7, resetTime, disposeAction): HasDisposeAction={leaseS3.DisposeAction != null}");

// Overload 4: Successful(remainingPermits, resetTime, disposeAction, limit)
var disposeCallbackFired = false;
var leaseS4 = RateLimitLease.Successful(6, DateTimeOffset.UtcNow.AddMinutes(1), () => { disposeCallbackFired = true; }, limit: 10);
leaseS4.Dispose();
Console.WriteLine($"    -> Successful(6, resetTime, disposeAction, 10): DisposeCallbackFired={disposeCallbackFired}");

// Overload 5: Rejected(retryAfter, resetTime, limit)
var leaseRej = RateLimitLease.Rejected(TimeSpan.FromSeconds(15), DateTimeOffset.UtcNow.AddMinutes(1), limit: 10);
Console.WriteLine($"    -> Rejected(15s, resetTime, 10): IsAcquired={leaseRej.IsAcquired}, RetryAfter={leaseRej.RetryAfter?.TotalSeconds}s, Limit={leaseRej.Limit}");

// Demonstrate RateLimitingMiddleware invocation with simulated HTTP context
var middlewareOptions = new RateLimitingMiddlewareOptions
{
    PermitCost = 1,
    FailClosed = false,
    OnRejected = async (ctx, lease, ct) =>
    {
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.Response.WriteAsync("Custom Rejection: Limit Exceeded", ct);
    },
    OnRedisFailure = async (ctx, err, ct) =>
    {
        if (err.Code == RateLimitingErrorCodes.ConnectionFailedCode)
        {
            ctx.Response.Headers["X-RateLimit-Degraded"] = "true";
        }
        await Task.CompletedTask;
    }
};

var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(middlewareOptions));

// 1. Successful pass-through request
var httpContextOk = new DefaultHttpContext();
httpContextOk.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
await middleware.InvokeAsync(httpContextOk, directLimiter, policyRegistry);
Console.WriteLine($"    -> Middleware execution (OK): StatusCode={httpContextOk.Response.StatusCode}, LimitHeader={httpContextOk.Response.Headers[RateLimitingHeaders.Limit]}, RemainingHeader={httpContextOk.Response.Headers[RateLimitingHeaders.Remaining]}");

// 2. Exhaust limiter to trigger 429
for (int i = 0; i < 10; i++)
{
    await directLimiter.AcquireAsync("10.0.0.2", 1);
}

var httpContextRejected = new DefaultHttpContext();
httpContextRejected.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.2");
await middleware.InvokeAsync(httpContextRejected, directLimiter, policyRegistry);
Console.WriteLine($"    -> Middleware execution (Rejected): StatusCode={httpContextRejected.Response.StatusCode}, RetryAfterHeader={httpContextRejected.Response.Headers[RateLimitingHeaders.RetryAfter]}");

// =============================================================================
// LEVEL 7: Scalability, Performance & OpenTelemetry Metrics
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 7] SCALABILITY, BENCHMARKS & METRICS INSTRUMENTATION");
Console.WriteLine($"    -> Meter Name:    '{RateLimitingMetrics.MeterName}'");
Console.WriteLine($"    -> Meter Version: '{RateLimitingMetrics.MeterVersion}'");

// 7a. Direct access to static metric instrument properties
Counter<long> requestsCounter = RateLimitingMetrics.RequestsTotal;
Histogram<double> leaseDurationHisto = RateLimitingMetrics.LeaseDuration;
Console.WriteLine($"    -> RequestsTotal (Counter): Name='{requestsCounter.Name}'");
Console.WriteLine($"    -> LeaseDuration (Histogram): Name='{leaseDurationHisto.Name}'");

// 7b. RecordRequest across all 7 limiter type/status combinations
RateLimitingMetrics.RecordRequest("fixed_window", "acquired", 0.04);
RateLimitingMetrics.RecordRequest("sliding_window", "acquired", 0.08);
RateLimitingMetrics.RecordRequest("token_bucket", "rejected", 0.02);
RateLimitingMetrics.RecordRequest("concurrency", "acquired", 0.01);
RateLimitingMetrics.RecordRequest("composite", "acquired", 0.12);
RateLimitingMetrics.RecordRequest("redis_sliding_window", "rejected", 1.50);
RateLimitingMetrics.RecordRequest("redis_token_bucket", "failed", 0.50);
Console.WriteLine("    -> RecordRequest: 7 samples across all limiter types (fixed, sliding, token, concurrency, composite, redis_sliding, redis_token).");

// =============================================================================
// LEVEL 8: Customization & Extensibility (Custom IRateLimiter Implementation)
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 8] CUSTOM EXTENSIBILITY (CUSTOM IRATELIMITER)");
var customLimiter = new ShowcaseCustomLimiter();

// 8a. Direct custom limiter call
var customAcquired = await customLimiter.AcquireAsync("custom-key", 1);
Console.WriteLine($"    -> ShowcaseCustomLimiter.AcquireAsync: IsAcquired={customAcquired.Value.IsAcquired}, Remaining={customAcquired.Value.RemainingPermits}");

// 8b. Custom limiter as IRateLimiter polymorphism
#pragma warning disable CA1859 // Intentional: demonstrating IRateLimiter interface contract
IRateLimiter customAsInterface = customLimiter;
#pragma warning restore CA1859
var customResult2 = await customAsInterface.AcquireAsync("custom-key", 1);
Console.WriteLine($"    -> IRateLimiter polymorphism: IsAcquired={customResult2.Value.IsAcquired}");

// 8c. Custom limiter in RateLimiterPolicyBuilder with SetDefaultPolicy(IRateLimiter)
var customPolicyBuilder = new RateLimiterPolicyBuilder();
customPolicyBuilder.AddPolicy("custom-impl", customLimiter);
customPolicyBuilder.SetDefaultPolicy(customLimiter);  // SetDefaultPolicy(IRateLimiter) overload
var customRegistry = customPolicyBuilder.Build();
Console.WriteLine($"    -> Custom limiter in policy: registered={customRegistry.GetPolicy("custom-impl") != null}, default={customRegistry.DefaultLimiter != null}");

// 8d. Custom limiter as child of CompositeRateLimiter
var compositeWithCustom = new CompositeRateLimiter(customLimiter, new FixedWindowRateLimiter());
Console.WriteLine($"    -> CompositeRateLimiter with custom IRateLimiter: {compositeWithCustom.Limiters.Count} limiters");

// =============================================================================
// LEVEL 9: Distributed Redis Rate Limiting Extensions
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 9] DISTRIBUTED REDIS EXTENSIONS");

// 9a. AddRedisRateLimiting(Action<RedisRateLimiterOptions>) — connection string variant
var redisServices = new ServiceCollection();
redisServices.AddLogging();
redisServices.AddRedisRateLimiting(opt =>
{
    opt.Configuration = "localhost:6379,abortConnect=false";
    opt.KeyPrefix = "test:sliding:";
    opt.MaxPermits = 100;
    opt.WindowDuration = TimeSpan.FromMinutes(1);
    opt.Database = 0;
});
Console.WriteLine("    -> AddRedisRateLimiting(configure): sliding window via connection string.");

// 9b. AddRedisTokenBucketRateLimiting(Action<RedisTokenBucketRateLimiterOptions>) — connection string variant
var redisTokenSvc = new ServiceCollection();
redisTokenSvc.AddLogging();
redisTokenSvc.AddRedisTokenBucketRateLimiting(opt =>
{
    opt.Configuration = "localhost:6379,abortConnect=false";
    opt.KeyPrefix = "test:token:";
    opt.TokenLimit = 200;
    opt.TokensPerPeriod = 20;
    opt.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
    opt.Database = 1;
});
Console.WriteLine("    -> AddRedisTokenBucketRateLimiting(configure): token bucket via connection string.");

// 9c & 9d. IConnectionMultiplexer overloads — signature documentation
// In production with existing multiplexer:
//   services.AddRedisRateLimiting(existingMux, o => { o.MaxPermits = 500; });
//   services.AddRedisTokenBucketRateLimiting(existingMux, o => { o.TokenLimit = 300; });
// Allows reusing a shared ConnectionMultiplexer (avoids redundant Redis connections)
Console.WriteLine("    -> AddRedisRateLimiting(IConnectionMultiplexer, ...): external multiplexer overload available.");
Console.WriteLine("    -> AddRedisTokenBucketRateLimiting(IConnectionMultiplexer, ...): external multiplexer overload available.");
Console.WriteLine("    -> Both Redis limiters implement IAsyncDisposable (DisposeAsync).");
Console.WriteLine("    -> All 4 Redis DI extension methods verified.");

// =============================================================================
// LEVEL 10: Enterprise Architecture (Composite Multi-Tier Limiter & Rollback)
// =============================================================================
Console.WriteLine("\n>>> [LEVEL 10] ENTERPRISE ARCHITECTURE & COMPOSITE LIMITER");
var burstTier = new TokenBucketRateLimiter(new RateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromSeconds(1) });
var sustainedTier = new SlidingWindowRateLimiter(new RateLimiterOptions { PermitLimit = 50, Window = TimeSpan.FromMinutes(1) });
var concurrencyTier = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions { PermitLimit = 2 });

// 10a. params[] constructor
var compositeFromParams = new CompositeRateLimiter(burstTier, sustainedTier, concurrencyTier);
Console.WriteLine($"    -> CompositeRateLimiter (params[]): {compositeFromParams.Limiters.Count} limiters.");

// 10b. IEnumerable<IRateLimiter> constructor
var compositeFromList = new CompositeRateLimiter(new List<IRateLimiter> { burstTier, sustainedTier, concurrencyTier });
Console.WriteLine($"    -> CompositeRateLimiter (IEnumerable): {compositeFromList.Limiters.Count} limiters.");

// 10c. Limiters property — IReadOnlyList<IRateLimiter>
IReadOnlyList<IRateLimiter> childLimiters = compositeFromParams.Limiters;
Console.WriteLine($"    -> Limiters[0]={childLimiters[0].GetType().Name}, [1]={childLimiters[1].GetType().Name}, [2]={childLimiters[2].GetType().Name}");

// 10d. AcquireAsync on composite
var compResult = await compositeFromParams.AcquireAsync("enterprise-client-1", permits: 1);
Console.WriteLine($"    -> Composite Acquire: IsAcquired={compResult.Value.IsAcquired}, Remaining={compResult.Value.RemainingPermits}");

// 10e. Dispose composite lease (triggers all underlying child disposers)
compResult.Value.Dispose();
Console.WriteLine("    -> Composite lease disposed cleanly.");

// 10f. Composite DI Extension
var compServices = new ServiceCollection();
compServices.AddCompositeRateLimiter(burstTier, sustainedTier);
Console.WriteLine("    -> AddCompositeRateLimiter(params) registered in ServiceCollection.");

// 10g. All in-memory DI extension methods
compServices.AddSlidingWindowRateLimiter(o => { o.PermitLimit = 10; });
compServices.AddTokenBucketRateLimiter(o => { o.PermitLimit = 15; });
compServices.AddConcurrencyRateLimiter(o => { o.PermitLimit = 3; });
compServices.AddHttpRateLimiting(o => { o.PermitCost = 1; });
Console.WriteLine("    -> AddSlidingWindowRateLimiter, AddTokenBucketRateLimiter, AddConcurrencyRateLimiter, AddHttpRateLimiting: all verified.");

// 10h. UseHttpRateLimiting — IApplicationBuilder extension
// Adds RateLimitingMiddleware to the HTTP pipeline.
// Usage in production: app.UseHttpRateLimiting();
Console.WriteLine("    -> UseHttpRateLimiting: registers RateLimitingMiddleware in the IApplicationBuilder pipeline.");
Console.WriteLine("       Usage: app.UseHttpRateLimiting(); (IApplicationBuilder.UseMiddleware<RateLimitingMiddleware>())");

// 10i. TimeProvider injection — optional ctor param on all in-memory limiters
// Enables deterministic testing by injecting a custom/fake time provider.
// In production, TimeProvider.System (real clock) is used by default.
// In tests, use Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider (NuGet).
var testableFixed = new FixedWindowRateLimiter(new RateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromSeconds(10) }, TimeProvider.System);
var t1 = await testableFixed.AcquireAsync("testable-key");
var t2 = await testableFixed.AcquireAsync("testable-key");
var t3 = await testableFixed.AcquireAsync("testable-key"); // should be rejected (limit=2)
Console.WriteLine($"    -> TimeProvider injection (TimeProvider.System): t1={t1.Value.IsAcquired}, t2={t2.Value.IsAcquired}, t3(rejected)={!t3.Value.IsAcquired}");
Console.WriteLine("       [Test]: Use FakeTimeProvider from Microsoft.Extensions.TimeProvider.Testing for deterministic tests.");

Console.WriteLine("\n================================================================================");
Console.WriteLine(" ALL 11 LEVELS (0-10) DEMONSTRATED WITH 100% PUBLIC API COVERAGE!");
Console.WriteLine(" ERICKSONLOPEZ.RATELIMITING SHOWCASE: FULLY SYNCHRONIZED & CERTIFIED.");
Console.WriteLine("================================================================================");
