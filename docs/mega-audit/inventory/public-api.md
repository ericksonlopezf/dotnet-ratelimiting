# PUBLIC API INVENTORY — EricksonLopez Rate Limiting Ecosystem

**Audit Timestamp:** 2026-09-05T04:45:00Z  
**Ecosystem Projects:** 
1. `EricksonLopez.RateLimiting` (Core Tier 0)
2. `EricksonLopez.RateLimiting.AspNetCore` (HTTP Tier 1)
3. `EricksonLopez.RateLimiting.Redis` (Distributed Tier 1)

---

## 1. Classification Taxonomy
Every exposed symbol is classified under the following governance dimensions:
- **Essential**: Core architectural primitive vital for operations.
- **Advanced**: Specialized feature for high-scale, multi-tenant, or distributed topologies.
- **Dangerous**: Prone to misuse, unhandled exceptions, resource exhaustion, or security bypass if misconfigured.
- **Redundant**: Duplicate functionality or backward-compatibility legacy alias.
- **Confusing**: Ambiguous semantics or conflicting behavior.
- **Inconsistent**: Diverges from ecosystem idioms (e.g. naming, allocation patterns, async signatures).
- **Candidate for Simplification**: High cyclomatic complexity or overly verbose ergonomics.
- **Candidate for Removal**: Deprecated or superseded API.

---

## 2. Assembly: `EricksonLopez.RateLimiting`

### 2.1 Namespace: `EricksonLopez.RateLimiting`

#### `IRateLimiter` (Interface)
- **Role:** Foundational abstraction for permit acquisition across in-memory and distributed throttlers.
- **Methods:**
  - `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)`
- **Classification:** **Essential**, **Inconsistent** (Returns `Task<Result<T>>` instead of `ValueTask<Result<T>>`, forcing heap allocation for synchronous in-memory fast paths despite `RateLimitLease` being a struct).

#### `RateLimitLease` (Readonly Record Struct, `IDisposable`)
- **Role:** Immutable lease token encapsulating acquisition status, quota, remaining permits, and reset metadata.
- **Properties:**
  - `bool IsAcquired { get; init; }` — **Essential**
  - `int RemainingPermits { get; init; }` — **Essential**
  - `TimeSpan? RetryAfter { get; init; }` — **Essential**
  - `DateTimeOffset? ResetTime { get; init; }` — **Essential**
  - `Action? DisposeAction { get; init; }` — **Advanced** (Allows concurrency slot reclamation upon disposal)
  - `int? Limit { get; init; }` — **Essential**
- **Methods:**
  - `void Dispose()` — **Essential**
  - `void Deconstruct(out bool, out int, out TimeSpan?, out DateTimeOffset?)` — **Redundant** (v1.0.0 legacy compatibility)
  - `void Deconstruct(out bool, out int, out TimeSpan?, out DateTimeOffset?, out Action?)` — **Advanced**
- **Static Factory Methods:**
  - `RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime = null)` — **Redundant**
  - `RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime, int? limit)` — **Essential**
  - `RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime, Action? disposeAction)` — **Advanced**
  - `RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime, Action? disposeAction, int? limit)` — **Essential**
  - `RateLimitLease Rejected(TimeSpan retryAfter, DateTimeOffset? resetTime = null, int? limit = null)` — **Essential**
- **Classification:** **Essential**. High efficiency stack allocation, correctly handles `DisposeAction?.Invoke()`.

#### `RateLimiterOptions` (Sealed Class)
- **Role:** Configuration options for windowed in-memory algorithms.
- **Properties:**
  - `int PermitLimit { get; set; }` (Default: 100, Guard: `< 1`) — **Essential**
  - `TimeSpan Window { get; set; }` (Default: 1m, Guard: `<= TimeSpan.Zero`) — **Essential**
  - `int SegmentsPerWindow { get; set; }` (Default: 6, Guard: `< 1`) — **Essential**
  - `int MaxPartitions { get; set; }` (Default: 10,000, Guard: `< 1`) — **Advanced**, **Dangerous** (If saturated by malicious distinct keys, subsequent new legitimate keys are rejected!).
- **Classification:** **Essential**, **Dangerous** (Unbounded key ingestion can lead to partition saturation DoS).

#### `ConcurrencyRateLimiterOptions` (Sealed Class)
- **Role:** Configuration options for in-flight concurrency throttling.
- **Properties:**
  - `int PermitLimit { get; set; }` (Default: 10, Guard: `< 1`) — **Essential**
  - `int MaxPartitions { get; set; }` (Default: 10,000, Guard: `< 1`) — **Advanced**, **Dangerous**
- **Classification:** **Essential**.

#### `FixedWindowRateLimiter` (Sealed Class : `IRateLimiter`)
- **Role:** Discrete fixed-window rate limiter with O(1) tick arithmetic.
- **Constructors:**
  - `FixedWindowRateLimiter(RateLimiterOptions? options = null, TimeProvider? timeProvider = null)` — **Essential**
- **Methods:**
  - `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)` — **Essential**
- **Classification:** **Essential**. High throughput, predictable boundary resets.

#### `SlidingWindowRateLimiter` (Sealed Class : `IRateLimiter`)
- **Role:** Segmented sliding-window rate limiter using internal ring buffer.
- **Constructors:**
  - `SlidingWindowRateLimiter(RateLimiterOptions? options = null, TimeProvider? timeProvider = null)` — **Essential**
- **Methods:**
  - `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)` — **Essential**
- **Classification:** **Essential**. Smooth boundary distribution, prevents window boundary burst doubling.

#### `TokenBucketRateLimiter` (Sealed Class : `IRateLimiter`)
- **Role:** Continuous token bucket with fractional token accrual.
- **Constructors:**
  - `TokenBucketRateLimiter(RateLimiterOptions? options = null, TimeProvider? timeProvider = null)` — **Essential**
- **Methods:**
  - `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)` — **Essential**
- **Classification:** **Essential**. Supports burst capacity and continuous smooth replenishment.

#### `ConcurrencyRateLimiter` (Sealed Class : `IRateLimiter`)
- **Role:** Concurrency rate limiter restricting in-flight parallel operations.
- **Constructors:**
  - `ConcurrencyRateLimiter(ConcurrencyRateLimiterOptions? options = null)` — **Essential**
- **Methods:**
  - `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)` — **Essential**
- **Classification:** **Essential**. Lock-free atomic reservation with safe partition retirement.

#### `CompositeRateLimiter` (Sealed Class : `IRateLimiter`)
- **Role:** Sequentially evaluates multiple child rate limiters with logical AND semantics.
- **Constructors:**
  - `CompositeRateLimiter(IEnumerable<IRateLimiter> limiters)` — **Essential**
  - `CompositeRateLimiter(params IRateLimiter[] limiters)` — **Essential**
- **Properties:**
  - `IReadOnlyList<IRateLimiter> Limiters { get; }` — **Essential**
- **Methods:**
  - `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)` — **Essential**, **Dangerous** (If a downstream limiter rejects, tokens already consumed in upstream non-concurrency limiters cannot be refunded!).
- **Classification:** **Advanced**, **Dangerous** (Permanent token leakage on secondary limiter rejection).

#### `RateLimitingErrorCodes` (Static Class)
- **Constants:**
  - `string ConnectionFailedCode = "RateLimit.Redis.ConnectionFailed"` — **Essential**
- **Classification:** **Essential**. Enables compile-time pattern matching for degradation handling.

#### `RateLimitingMetrics` (Static Class)
- **Constants & Fields:**
  - `string MeterName = "EricksonLopez.RateLimiting"` — **Essential**
  - `string MeterVersion = "1.0.0"` — **Essential**
  - `Counter<long> RequestsTotal` — **Essential**
  - `Histogram<double> LeaseDuration` — **Essential**
- **Methods:**
  - `void RecordRequest(string limiterType, string status, double durationMs)` — **Essential**
- **Classification:** **Essential**. OpenTelemetry compliant, strictly bounded label cardinality.

#### `RateLimitingServiceCollectionExtensions` (Static Class)
- **Extension Methods:**
  - `IServiceCollection AddSlidingWindowRateLimiter(this IServiceCollection, Action<RateLimiterOptions>?)` — **Essential**
  - `IServiceCollection AddTokenBucketRateLimiter(this IServiceCollection, Action<RateLimiterOptions>?)` — **Essential**
  - `IServiceCollection AddFixedWindowRateLimiter(this IServiceCollection, Action<RateLimiterOptions>?)` — **Essential**
  - `IServiceCollection AddConcurrencyRateLimiter(this IServiceCollection, Action<ConcurrencyRateLimiterOptions>?)` — **Essential**
  - `IServiceCollection AddCompositeRateLimiter(this IServiceCollection, params IRateLimiter[])` — **Essential**
- **Classification:** **Essential**. Clean DI registration primitives.

---

### 2.2 Namespace: `EricksonLopez.RateLimiting.Policies`

#### `IRateLimiterPolicy` (Interface)
- **Properties:**
  - `string Name { get; }` — **Essential**
  - `IRateLimiter Limiter { get; }` — **Essential**
- **Classification:** **Essential**.

#### `IRateLimiterPolicyRegistry` (Interface)
- **Properties:**
  - `IRateLimiter? DefaultLimiter { get; set; }` — **Essential**
- **Methods:**
  - `void Register(string name, IRateLimiter limiter)` — **Essential**
  - `IRateLimiter? GetPolicy(string name)` — **Essential**
- **Classification:** **Essential**.

#### `RateLimiterPolicy` (Sealed Class : `IRateLimiterPolicy`)
- **Constructors:**
  - `RateLimiterPolicy(string name, IRateLimiter limiter)` — **Essential**
- **Classification:** **Essential**.

#### `RateLimiterPolicyBuilder` (Sealed Class)
- **Methods:**
  - `RateLimiterPolicyBuilder AddFixedWindow(string, Action<RateLimiterOptions>, TimeProvider? = null)` — **Essential**
  - `RateLimiterPolicyBuilder AddSlidingWindow(string, Action<RateLimiterOptions>, TimeProvider? = null)` — **Essential**
  - `RateLimiterPolicyBuilder AddTokenBucket(string, Action<RateLimiterOptions>, TimeProvider? = null)` — **Essential**
  - `RateLimiterPolicyBuilder AddConcurrency(string, Action<ConcurrencyRateLimiterOptions>)` — **Essential**
  - `RateLimiterPolicyBuilder AddComposite(string, params IRateLimiter[])` — **Essential**
  - `RateLimiterPolicyBuilder AddPolicy(string, IRateLimiter)` — **Essential**
  - `RateLimiterPolicyBuilder SetDefaultPolicy(IRateLimiter)` — **Essential**
  - `RateLimiterPolicyBuilder SetDefaultPolicy(string)` — **Essential**
  - `IRateLimiterPolicyRegistry Build()` — **Essential**
- **Classification:** **Essential**. Exceptional developer experience with fluent API ergonomics.

#### `RateLimiterPolicyRegistry` (Sealed Class : `IRateLimiterPolicyRegistry`)
- **Classification:** **Essential**. Case-insensitive lookup via `StringComparer.OrdinalIgnoreCase`.

---

## 3. Assembly: `EricksonLopez.RateLimiting.AspNetCore`

### 3.1 Namespace: `EricksonLopez.RateLimiting.AspNetCore`

#### `RateLimitingMiddleware` (Sealed Class)
- **Constructors:**
  - `RateLimitingMiddleware(RequestDelegate next, IOptions<RateLimitingMiddlewareOptions> options)` — **Essential**
- **Methods:**
  - `Task InvokeAsync(HttpContext context, IRateLimiter? rateLimiter = null, IRateLimiterPolicyRegistry? policyRegistry = null)` — **Essential**
- **Classification:** **Essential**. Complete lifecycle enforcement, header injection, fail-open/fail-closed branching, endpoint metadata extraction.

#### `RateLimitingMiddlewareOptions` (Sealed Class)
- **Properties:**
  - `Func<HttpContext, string> PartitionKeyResolver { get; set; }` — **Essential**, **Dangerous** (Default IP resolver is blind to proxies without `ForwardedHeadersMiddleware`).
  - `int PermitCost { get; set; }` (Default: 1, Guard: `< 1`) — **Essential**
  - `bool FailClosed { get; set; }` (Default: false / Fail-Open) — **Essential**
  - `Func<HttpContext, RateLimitLease, CancellationToken, Task>? OnRejected { get; set; }` — **Advanced**
  - `Func<HttpContext, Error, CancellationToken, Task>? OnRedisFailure { get; set; }` — **Advanced**, **Dangerous** (If configured, execution halts and `_next` is NEVER called, acting as a terminal handler).
- **Classification:** **Essential**.

#### `RateLimitingHeaders` (Static Class)
- **Constants:**
  - `string Limit = "X-RateLimit-Limit"` — **Essential**
  - `string Remaining = "X-RateLimit-Remaining"` — **Essential**
  - `string Reset = "X-RateLimit-Reset"` — **Essential**
  - `string RetryAfter = "Retry-After"` — **Essential**
- **Classification:** **Essential**. Conforms to RFC drafts for RateLimit header fields.

#### `EnableRateLimitingAttribute` (Sealed Attribute : `IEnableRateLimitingMetadata`)
- **Constructors:**
  - `EnableRateLimitingAttribute(string policyName)` — **Essential**
- **Classification:** **Essential**. Declarative controller/action attribute.

#### `DisableRateLimitingAttribute` (Sealed Attribute : `IDisableRateLimitingMetadata`)
- **Constructors:**
  - `DisableRateLimitingAttribute()` — **Essential**
- **Classification:** **Essential**. Declarative endpoint exclusion.

#### `IEnableRateLimitingMetadata` / `IDisableRateLimitingMetadata` (Interfaces)
- **Classification:** **Essential**. Minimal API endpoint metadata marker interfaces.

#### `EndpointRateLimitingExtensions` (Static Class)
- **Methods:**
  - `RequireRateLimiting<TBuilder>(this TBuilder, string policyName)` — **Essential**
  - `DisableRateLimiting<TBuilder>(this TBuilder)` — **Essential**
  - `RequireDistributedRateLimiting<TBuilder>(this TBuilder, string policyName)` — **Redundant** (Alias)
  - `DisableDistributedRateLimiting<TBuilder>(this TBuilder)` — **Redundant** (Alias)
- **Classification:** **Essential**. Clean integration with `RouteGroupBuilder` and `RouteHandlerBuilder`.

#### `RateLimitingAspNetCoreExtensions` (Static Class)
- **Methods:**
  - `IServiceCollection AddHttpRateLimiting(this IServiceCollection, Action<RateLimitingMiddlewareOptions>?)` — **Essential**
  - `IServiceCollection AddRateLimiting(this IServiceCollection, Action<RateLimiterPolicyBuilder>, Action<RateLimitingMiddlewareOptions>?)` — **Essential**
  - `IApplicationBuilder UseHttpRateLimiting(this IApplicationBuilder)` — **Essential**
- **Classification:** **Essential**.

---

## 4. Assembly: `EricksonLopez.RateLimiting.Redis`

### 4.1 Namespace: `EricksonLopez.RateLimiting.Redis`

#### `RedisRateLimiterOptions` (Sealed Class)
- **Properties:**
  - `string Configuration { get; set; }` — **Essential**
  - `string KeyPrefix { get; set; }` (Default: "rl:") — **Essential**
  - `TimeSpan WindowDuration { get; set; }` (Guard: `<= TimeSpan.Zero`) — **Essential**
  - `int MaxPermits { get; set; }` (Guard: `< 1`) — **Essential**
  - `int Database { get; set; }` (Guard: `< 0 || > 15`) — **Essential**
- **Classification:** **Essential**. Strong option boundary validation.

#### `RedisTokenBucketRateLimiterOptions` (Sealed Class)
- **Properties:**
  - `string Configuration { get; set; }` — **Essential**
  - `string KeyPrefix { get; set; }` (Default: "rl:tb:") — **Essential**
  - `int TokenLimit { get; set; }` (Guard: `< 1`) — **Essential**
  - `int TokensPerPeriod { get; set; }` (Guard: `< 1`) — **Essential**
  - `TimeSpan ReplenishmentPeriod { get; set; }` (Guard: `<= TimeSpan.Zero`) — **Essential**
  - `int Database { get; set; }` (Guard: `< 0 || > 15`) — **Essential**
- **Classification:** **Essential**.

#### `RedisSlidingWindowRateLimiter` (Sealed Class : `IRateLimiter`, `IAsyncDisposable`)
- **Constructors:**
  - `RedisSlidingWindowRateLimiter(IConnectionMultiplexer, IOptions<RedisRateLimiterOptions>, ILogger<RedisSlidingWindowRateLimiter>)` — **Essential**
- **Methods:**
  - `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)` — **Essential**
  - `ValueTask DisposeAsync()` — **Essential**
- **Classification:** **Essential**. Atomic Lua script over ZSET. 100% Native AOT compatible.

#### `RedisTokenBucketRateLimiter` (Sealed Class : `IRateLimiter`, `IAsyncDisposable`)
- **Constructors:**
  - `RedisTokenBucketRateLimiter(IConnectionMultiplexer, IOptions<RedisTokenBucketRateLimiterOptions>, ILogger<RedisTokenBucketRateLimiter>)` — **Essential**
- **Methods:**
  - `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)` — **Essential**
  - `ValueTask DisposeAsync()` — **Essential**
- **Classification:** **Essential**. Atomic Lua script over Hash. Zero JSON/Reflection.

#### `RateLimitingRedisServiceCollectionExtensions` (Static Class)
- **Methods:**
  - `AddRedisRateLimiting(this IServiceCollection, Action<RedisRateLimiterOptions>)` — **Essential**
  - `AddRedisRateLimiting(this IServiceCollection, IConnectionMultiplexer, Action<RedisRateLimiterOptions>?)` — **Essential**
  - `AddRedisTokenBucketRateLimiting(this IServiceCollection, Action<RedisTokenBucketRateLimiterOptions>)` — **Essential**
  - `AddRedisTokenBucketRateLimiting(this IServiceCollection, IConnectionMultiplexer, Action<RedisTokenBucketRateLimiterOptions>?)` — **Essential**
- **Classification:** **Essential**. Supports standalone auto-connection or shared singleton `IConnectionMultiplexer`.
