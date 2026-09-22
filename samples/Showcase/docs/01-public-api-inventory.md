# Phase 1 · Exhaustive Public API Inventory

This document provides the complete, audited, and verified inventory of the public API surface exposed exclusively by the **Core Library** and **Infrastructure** projects:
1. `EricksonLopez.RateLimiting` (Core)
2. `EricksonLopez.RateLimiting.AspNetCore` (Infrastructure)
3. `EricksonLopez.RateLimiting.Redis` (Infrastructure)

All internal elements (such as `ConcurrencyAcquireResult`, `FixedWindowPartition`, `SlidingWindowPartition`, `TokenBucketPartition`, `ConcurrencyPartition`, `RateLimitErrors`, `Log`) are deliberately excluded.

---

## 1. Inventory of Public Types and Contracts

| Name | Namespace | Responsibility | Dependencies | Use Cases | Complexity | Showcase Coverage |
|---|---|---|---|---|---|---|
| `IRateLimiter` | `EricksonLopez.RateLimiting` | Core contract for rate limiting algorithms and per-partition permit acquisition. | `EricksonLopez.Result.Result<T>`, `CancellationToken`, `Task` | Primary abstraction for dependency injection and algorithm decoupling. | Basic | Covered |
| `RateLimitLease` | `EricksonLopez.RateLimiting` | Immutable structure (`readonly record struct`) representing permit acquisition result and metadata. Implements `IDisposable`. | `System.DateTimeOffset`, `System.TimeSpan`, `System.Action` | Evaluation of success/rejection, remaining quota, reset time, retry-after, and concurrent slot release. | Basic / Intermediate | Covered |
| `RateLimiterOptions` | `EricksonLopez.RateLimiting` | Strongly-typed options for in-memory limiters (Fixed, Sliding, Token Bucket). | `System.TimeSpan` | Parameterization of PermitLimit, Window, SegmentsPerWindow, and MaxPartitions. | Basic | Covered |
| `ConcurrencyRateLimiterOptions` | `EricksonLopez.RateLimiting` | Configuration options for the in-flight concurrency limiter. | None | Restricting concurrent executions (`PermitLimit`, `MaxPartitions`). | Intermediate | Covered |
| `FixedWindowRateLimiter` | `EricksonLopez.RateLimiting` | High-throughput in-memory discrete fixed window implementation. Implements `IRateLimiter`. | `RateLimiterOptions`, `TimeProvider`, `RateLimitingMetrics` | Uniform time-block quotas with minimum CPU and memory overhead. | Basic | Covered |
| `SlidingWindowRateLimiter` | `EricksonLopez.RateLimiting` | In-memory segmented sliding window algorithm smoothing boundary burst spikes. Implements `IRateLimiter`. | `RateLimiterOptions`, `TimeProvider`, `RateLimitingMetrics` | High-precision quotas preventing traffic spikes at window boundaries. | Intermediate | Covered |
| `TokenBucketRateLimiter` | `EricksonLopez.RateLimiting` | In-memory continuous token replenishment algorithm with controlled burst capacity. Implements `IRateLimiter`. | `RateLimiterOptions`, `TimeProvider`, `RateLimitingMetrics` | APIs tolerating initial bursts while enforcing a steady long-term refill rate. | Intermediate | Covered |
| `ConcurrencyRateLimiter` | `EricksonLopez.RateLimiting` | Lock-free in-memory concurrency limiter based on atomic slots released via lease disposal. Implements `IRateLimiter`. | `ConcurrencyRateLimiterOptions`, `RateLimitingMetrics` | Preventing thread pool starvation and heavy resource exhaustion (e.g., file processing, reports). | Advanced | Covered |
| `CompositeRateLimiter` | `EricksonLopez.RateLimiting` | Composite limiter evaluating multiple limiters sequentially with AND logic and automatic rollback upon rejection. Implements `IRateLimiter`. | `IRateLimiter`, `RateLimitingMetrics` | Multi-dimensional rules: burst per second AND sustained quota per minute AND max concurrency. | Advanced | Covered |
| `RateLimitingMetrics` | `EricksonLopez.RateLimiting` | Native metric instrumentation compatible with OpenTelemetry (`System.Diagnostics.Metrics`). | `System.Diagnostics.Metrics.Meter`, `Counter<T>`, `Histogram<T>` | Production telemetry: request counter partitioned by type/status and lease latency distribution. | Intermediate | Covered |
| `RateLimitingErrorCodes` | `EricksonLopez.RateLimiting` | Public error code constants eliminating magic strings in failure callbacks. | None | Pattern-matching in `OnRedisFailure` callbacks (`RateLimit.Redis.ConnectionFailed`). | Basic | Covered |
| `RateLimitingServiceCollectionExtensions` | `EricksonLopez.RateLimiting` | Extension methods for registering in-memory and composite limiters in Microsoft Dependency Injection. | `Microsoft.Extensions.DependencyInjection.IServiceCollection` | Fluent singleton registration in .NET and ASP.NET Core applications. | Basic | Covered |
| `IRateLimiterPolicy` | `EricksonLopez.RateLimiting.Policies` | Interface binding a policy name with an `IRateLimiter` instance. | `IRateLimiter` | Named policy contract for differentiated endpoint routing. | Basic | Covered |
| `RateLimiterPolicy` | `EricksonLopez.RateLimiting.Policies` | Immutable, sealed implementation of `IRateLimiterPolicy`. | `IRateLimiter` | Encapsulation of policy name and limiter instance. | Basic | Covered |
| `IRateLimiterPolicyRegistry` | `EricksonLopez.RateLimiting.Policies` | Registry for rate limiting policies with default policy fallback support (`DefaultLimiter`). | `IRateLimiter` | Dynamic runtime resolution of rate limiters by policy name. | Intermediate | Covered |
| `RateLimiterPolicyRegistry` | `EricksonLopez.RateLimiting.Policies` | Thread-safe (`ConcurrentDictionary`) implementation of `IRateLimiterPolicyRegistry`. | `IRateLimiter` | In-memory store for named policies and default fallback. | Intermediate | Covered |
| `RateLimiterPolicyBuilder` | `EricksonLopez.RateLimiting.Policies` | Fluent builder for declarative configuration of named policies and default fallback. | `RateLimiterOptions`, `ConcurrencyRateLimiterOptions`, `IRateLimiterPolicyRegistry` | Clean setup of user tiers (Free, Pro, Enterprise, Streaming) at application startup. | Intermediate | Covered |
| `RateLimitingHeaders` | `EricksonLopez.RateLimiting.AspNetCore` | Standard HTTP rate limiting header name constants (IETF RFC Draft). | None | Injection and inspection of HTTP headers: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`, `Retry-After`. | Basic | Covered |
| `IDisableRateLimitingMetadata` | `EricksonLopez.RateLimiting.AspNetCore` | Marker metadata to disable rate limiting on specific endpoints. | None | Exempting health checks, documentation, or metrics endpoints. | Basic | Covered |
| `IEnableRateLimitingMetadata` | `EricksonLopez.RateLimiting.AspNetCore` | Endpoint metadata specifying the policy name to apply. | None | Declarative binding between HTTP endpoint and registered policy. | Basic | Covered |
| `DisableRateLimitingAttribute` | `EricksonLopez.RateLimiting.AspNetCore` | Attribute to disable rate limiting on controllers or action methods. Implements `IDisableRateLimitingMetadata`. | `System.Attribute` | Used on exempt MVC controllers or action methods. | Basic | Covered |
| `EnableRateLimitingAttribute` | `EricksonLopez.RateLimiting.AspNetCore` | Attribute binding an endpoint or controller to a named policy. Implements `IEnableRateLimitingMetadata`. | `System.Attribute` | Used on MVC controllers or actions with specific policy requirements. | Basic | Covered |
| `RateLimitingMiddlewareOptions` | `EricksonLopez.RateLimiting.AspNetCore` | Configuration options for the ASP.NET Core rate limiting middleware. | `Microsoft.AspNetCore.Http.HttpContext`, `RateLimitLease`, `EricksonLopez.Result.Error` | Partition key resolver, permit cost, Redis failure mode (`FailClosed`), `OnRejected` callback, `OnRedisFailure` callback. | Intermediate / Advanced | Covered |
| `RateLimitingMiddleware` | `EricksonLopez.RateLimiting.AspNetCore` | ASP.NET Core middleware evaluating policies, injecting headers, and intercepting rejected requests with 429 or 503. | `Microsoft.AspNetCore.Http.RequestDelegate`, `IOptions<RateLimitingMiddlewareOptions>`, `IRateLimiter`, `IRateLimiterPolicyRegistry` | Integration into the ASP.NET Core pipeline for perimeter traffic control. | Advanced | Covered |
| `RateLimitingAspNetCoreExtensions` | `EricksonLopez.RateLimiting.AspNetCore` | Extension methods for registering and configuring rate limiting middleware in ASP.NET Core. | `IServiceCollection`, `IApplicationBuilder`, `RateLimiterPolicyBuilder` | `AddHttpRateLimiting`, `AddRateLimiting`, `UseHttpRateLimiting`. | Basic / Intermediate | Covered |
| `EndpointRateLimitingExtensions` | `EricksonLopez.RateLimiting.AspNetCore` | Extension methods on `IEndpointConventionBuilder` for Minimal APIs. | `Microsoft.AspNetCore.Builder.IEndpointConventionBuilder` | `RequireRateLimiting`, `DisableRateLimiting`, and backward-compatible aliases `RequireDistributedRateLimiting`, `DisableDistributedRateLimiting`. | Basic | Covered |
| `RedisRateLimiterOptions` | `EricksonLopez.RateLimiting.Redis` | Configuration options for the distributed Redis sliding window limiter. | `System.TimeSpan` | Connection string, KeyPrefix, WindowDuration, MaxPermits, Database index (0-15). | Intermediate | Covered |
| `RedisTokenBucketRateLimiterOptions` | `EricksonLopez.RateLimiting.Redis` | Configuration options for the distributed Redis token bucket limiter. | `System.TimeSpan` | Connection string, KeyPrefix, TokenLimit, TokensPerPeriod, ReplenishmentPeriod, Database index (0-15). | Intermediate | Covered |
| `RedisSlidingWindowRateLimiter` | `EricksonLopez.RateLimiting.Redis` | Distributed Redis rate limiter executing an atomic Lua script over Sorted Sets (ZSET). Implements `IRateLimiter` and `IAsyncDisposable`. | `StackExchange.Redis.IConnectionMultiplexer`, `IOptions<RedisRateLimiterOptions>`, `ILogger`, `RateLimitingMetrics` | Unified rate limiting across multiple container replicas in a cluster. | Advanced | Covered |
| `RedisTokenBucketRateLimiter` | `EricksonLopez.RateLimiting.Redis` | Distributed Redis rate limiter executing an atomic Lua script over Hashes for continuous token replenishment. Implements `IRateLimiter` and `IAsyncDisposable`. | `StackExchange.Redis.IConnectionMultiplexer`, `IOptions<RedisTokenBucketRateLimiterOptions>`, `ILogger`, `RateLimitingMetrics` | Controlled bursts and granular refills in horizontally scaled environments. | Advanced | Covered |
| `RateLimitingRedisServiceCollectionExtensions` | `EricksonLopez.RateLimiting.Redis` | `IServiceCollection` extensions for registering distributed Redis limiters (Sliding Window and Token Bucket). | `Microsoft.Extensions.DependencyInjection.IServiceCollection`, `StackExchange.Redis.IConnectionMultiplexer` | Dependency injection registration via connection string or existing `IConnectionMultiplexer` singleton. | Intermediate | Covered |

---

## 2. Public Methods and Overloads in Inventory

### A. `IRateLimiter`
1. `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default);`

### B. `RateLimitLease`
1. Constructors:
   - `RateLimitLease(bool isAcquired, int remainingPermits, TimeSpan? retryAfter, DateTimeOffset? resetTime)`
   - `RateLimitLease(bool isAcquired, int remainingPermits, TimeSpan? retryAfter, DateTimeOffset? resetTime, Action? disposeAction)`
   - `RateLimitLease(bool IsAcquired, int RemainingPermits, TimeSpan? RetryAfter, DateTimeOffset? ResetTime, Action? DisposeAction, int? Limit)`
2. Deconstructors:
   - `void Deconstruct(out bool isAcquired, out int remainingPermits, out TimeSpan? retryAfter, out DateTimeOffset? resetTime)`
   - `void Deconstruct(out bool isAcquired, out int remainingPermits, out TimeSpan? retryAfter, out DateTimeOffset? resetTime, out Action? disposeAction)`
3. Static Factory Methods:
   - `RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime = null)`
   - `RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime, int? limit)`
   - `RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime, Action? disposeAction)`
   - `RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime, Action? disposeAction, int? limit)`
   - `RateLimitLease Rejected(TimeSpan retryAfter, DateTimeOffset? resetTime = null, int? limit = null)`
4. `void Dispose()`

### C. In-Memory Limiter Constructors
1. `FixedWindowRateLimiter(RateLimiterOptions? options = null, TimeProvider? timeProvider = null)`
2. `SlidingWindowRateLimiter(RateLimiterOptions? options = null, TimeProvider? timeProvider = null)`
3. `TokenBucketRateLimiter(RateLimiterOptions? options = null, TimeProvider? timeProvider = null)`
4. `ConcurrencyRateLimiter(ConcurrencyRateLimiterOptions? options = null)`
5. `CompositeRateLimiter(IEnumerable<IRateLimiter> limiters)`
6. `CompositeRateLimiter(params IRateLimiter[] limiters)`

### D. `CompositeRateLimiter`
1. Property: `IReadOnlyList<IRateLimiter> Limiters { get; }`
2. `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)`

### E. `RateLimiterPolicyBuilder`
1. `RateLimiterPolicyBuilder AddFixedWindow(string policyName, Action<RateLimiterOptions> configure, TimeProvider? timeProvider = null)`
2. `RateLimiterPolicyBuilder AddSlidingWindow(string policyName, Action<RateLimiterOptions> configure, TimeProvider? timeProvider = null)`
3. `RateLimiterPolicyBuilder AddTokenBucket(string policyName, Action<RateLimiterOptions> configure, TimeProvider? timeProvider = null)`
4. `RateLimiterPolicyBuilder AddConcurrency(string policyName, Action<ConcurrencyRateLimiterOptions> configure)`
5. `RateLimiterPolicyBuilder AddComposite(string policyName, params IRateLimiter[] limiters)`
6. `RateLimiterPolicyBuilder AddPolicy(string policyName, IRateLimiter limiter)`
7. `RateLimiterPolicyBuilder SetDefaultPolicy(IRateLimiter limiter)`
8. `RateLimiterPolicyBuilder SetDefaultPolicy(string policyName)`
9. `IRateLimiterPolicyRegistry Build()`

### F. `IRateLimiterPolicyRegistry` & `RateLimiterPolicyRegistry`
1. Property: `IRateLimiter? DefaultLimiter { get; set; }`
2. `void Register(string name, IRateLimiter limiter)`
3. `IRateLimiter? GetPolicy(string name)`

### G. In-Memory Dependency Injection (`RateLimitingServiceCollectionExtensions`)
1. `IServiceCollection AddFixedWindowRateLimiter(this IServiceCollection services, Action<RateLimiterOptions>? configure = null)`
2. `IServiceCollection AddSlidingWindowRateLimiter(this IServiceCollection services, Action<RateLimiterOptions>? configure = null)`
3. `IServiceCollection AddTokenBucketRateLimiter(this IServiceCollection services, Action<RateLimiterOptions>? configure = null)`
4. `IServiceCollection AddConcurrencyRateLimiter(this IServiceCollection services, Action<ConcurrencyRateLimiterOptions>? configure = null)`
5. `IServiceCollection AddCompositeRateLimiter(this IServiceCollection services, params IRateLimiter[] limiters)`

### H. ASP.NET Core Integration (`RateLimitingAspNetCoreExtensions`)
1. `IServiceCollection AddHttpRateLimiting(this IServiceCollection services, Action<RateLimitingMiddlewareOptions>? configure = null)`
2. `IServiceCollection AddRateLimiting(this IServiceCollection services, Action<RateLimiterPolicyBuilder> configure, Action<RateLimitingMiddlewareOptions>? configureMiddleware = null)`
3. `IApplicationBuilder UseHttpRateLimiting(this IApplicationBuilder app)`

### I. Endpoint Conventions (`EndpointRateLimitingExtensions`)
1. `TBuilder RequireRateLimiting<TBuilder>(this TBuilder builder, string policyName) where TBuilder : IEndpointConventionBuilder`
2. `TBuilder DisableRateLimiting<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder`
3. `TBuilder RequireDistributedRateLimiting<TBuilder>(this TBuilder builder, string policyName) where TBuilder : IEndpointConventionBuilder`
4. `TBuilder DisableDistributedRateLimiting<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder`

### J. Distributed Redis Limiters
1. `RedisSlidingWindowRateLimiter(IConnectionMultiplexer connection, IOptions<RedisRateLimiterOptions> options, ILogger<RedisSlidingWindowRateLimiter> logger)`
2. `ValueTask RedisSlidingWindowRateLimiter.DisposeAsync()`
3. `RedisTokenBucketRateLimiter(IConnectionMultiplexer connection, IOptions<RedisTokenBucketRateLimiterOptions> options, ILogger<RedisTokenBucketRateLimiter> logger)`
4. `ValueTask RedisTokenBucketRateLimiter.DisposeAsync()`

### K. Redis Dependency Injection (`RateLimitingRedisServiceCollectionExtensions`)
1. `IServiceCollection AddRedisRateLimiting(this IServiceCollection services, Action<RedisRateLimiterOptions> configure)`
2. `IServiceCollection AddRedisRateLimiting(this IServiceCollection services, IConnectionMultiplexer connectionMultiplexer, Action<RedisRateLimiterOptions>? configure = null)`
3. `IServiceCollection AddRedisTokenBucketRateLimiting(this IServiceCollection services, Action<RedisTokenBucketRateLimiterOptions> configure)`
4. `IServiceCollection AddRedisTokenBucketRateLimiting(this IServiceCollection services, IConnectionMultiplexer connectionMultiplexer, Action<RedisTokenBucketRateLimiterOptions>? configure = null)`

### L. Metrics and Instrumentation
1. `const string RateLimitingMetrics.MeterName`
2. `const string RateLimitingMetrics.MeterVersion`
3. `Counter<long> RateLimitingMetrics.RequestsTotal`
4. `Histogram<double> RateLimitingMetrics.LeaseDuration`
5. `void RateLimitingMetrics.RecordRequest(string limiterType, string status, double durationMs)`

### M. Error Codes
1. `const string RateLimitingErrorCodes.ConnectionFailedCode = "RateLimit.Redis.ConnectionFailed"`

### N. HTTP Header Constants
1. `const string RateLimitingHeaders.Limit = "X-RateLimit-Limit"`
2. `const string RateLimitingHeaders.Remaining = "X-RateLimit-Remaining"`
3. `const string RateLimitingHeaders.Reset = "X-RateLimit-Reset"`
4. `const string RateLimitingHeaders.RetryAfter = "Retry-After"`
