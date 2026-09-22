# Phase 5 · Technical API Reference (Microsoft Learn Style)

Formal technical reference documentation for all public types and methods in the `EricksonLopez.RateLimiting` public API inventory.

---

## 1. `IRateLimiter.AcquireAsync`

### Signature
```csharp
Task<Result<RateLimitLease>> AcquireAsync(
    string key, 
    int permits = 1, 
    CancellationToken cancellationToken = default);
```

### Parameters
- `key` (`string`): Unique string identifying the target partition to throttle (e.g. client IP, tenant ID, or user ID). Cannot be null.
- `permits` (`int`): Quantity of permits requested in the operation. Must be greater than or equal to 1 (default: `1`).
- `cancellationToken` (`CancellationToken`): Cancellation token used to cancel the asynchronous operation.

### Return Value
`Task<Result<RateLimitLease>>`: A `Result` container encapsulating `RateLimitLease`. When evaluation succeeds, the lease indicates whether permits were granted (`IsAcquired`) along with retry metadata (`RetryAfter`) and reset timestamp (`ResetTime`). If an infrastructure failure occurs (e.g. Redis socket timeout), `Result.IsFailure` is returned.

### Exceptions
- `ArgumentNullException`: When `key` is `null`.
- `ArgumentOutOfRangeException`: When `permits` is less than 1.
- `OperationCanceledException`: When the `cancellationToken` requests cancellation before or during evaluation.

### Remarks
In in-memory implementations (`FixedWindowRateLimiter`, `SlidingWindowRateLimiter`, `TokenBucketRateLimiter`, `ConcurrencyRateLimiter`), permit evaluation is a purely synchronous $O(1)$ operation wrapped in `Task.FromResult`. The cancellation token is checked at the start of the method to fulfill the interface contract. In distributed Redis implementations (`RedisSlidingWindowRateLimiter`, `RedisTokenBucketRateLimiter`), the cancellation token is propagated directly to the underlying StackExchange.Redis network invocation.

### Basic Example
```csharp
IRateLimiter limiter = new FixedWindowRateLimiter(new RateLimiterOptions { PermitLimit = 10 });
var result = await limiter.AcquireAsync("client-ip-127.0.0.1", permits: 1);

if (result.IsSuccess && result.Value.IsAcquired)
{
    Console.WriteLine($"Permit acquired. Remaining: {result.Value.RemainingPermits}");
}
```

### Advanced Example
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
var result = await limiter.AcquireAsync("tenant-enterprise-42", permits: 5, cts.Token);

if (result.IsFailure)
{
    Console.WriteLine($"Infrastructure failure: {result.Error.Code} - {result.Error.Description}");
    return;
}

using var lease = result.Value;
if (lease.IsAcquired)
{
    await ExecuteHeavyWorkloadAsync();
}
else
{
    Console.WriteLine($"Request rejected. Retry after: {lease.RetryAfter?.TotalSeconds}s");
}
```

### Best Practices
- Always dispose of the returned `RateLimitLease` (e.g. via `using var lease`) when working with `ConcurrencyRateLimiter` or `CompositeRateLimiter` to ensure timely release of concurrency slots.
- Normalize partition keys (e.g. lowercase and trimmed) to ensure cache consistency across in-memory and Redis lookups.

### Performance
- **In-Memory**: $O(1)$ complexity, ~50-100 nanoseconds per acquisition, zero heap allocations in steady state (due to `readonly record struct RateLimitLease`).
- **Redis**: Single network round-trip executing a precompiled atomic Lua script on the Redis server (~1-2 ms on local networks).

### Common Errors
- Passing `permits = 0` (throws `ArgumentOutOfRangeException`).
- Passing empty or null keys (throws `ArgumentNullException`).

### When to Use
- At any boundary or flow control point where request frequency must be constrained before allocating CPU, memory, database connections, or external API quotas.

### When NOT to Use
- To implement unbounded passive wait queues; this API performs immediate evaluation (grants or rejects with `RetryAfter`).

---

## 2. Factory Methods for `RateLimitLease`

### Signatures
```csharp
public static RateLimitLease Successful(
    int remainingPermits, 
    DateTimeOffset? resetTime = null);

public static RateLimitLease Successful(
    int remainingPermits, 
    DateTimeOffset? resetTime, 
    int? limit);

public static RateLimitLease Successful(
    int remainingPermits, 
    DateTimeOffset? resetTime, 
    Action? disposeAction);

public static RateLimitLease Successful(
    int remainingPermits, 
    DateTimeOffset? resetTime, 
    Action? disposeAction, 
    int? limit);

public static RateLimitLease Rejected(
    TimeSpan retryAfter, 
    DateTimeOffset? resetTime = null, 
    int? limit = null);
```

### Parameters
- `remainingPermits` (`int`): Quantity of remaining permits available in the current window.
- `resetTime` (`DateTimeOffset?`): Absolute timestamp when the quota resets.
- `limit` (`int?`): Total configured quota for the policy.
- `disposeAction` (`Action?`): Delegate invoked when the lease is disposed (used to release concurrency slots).
- `retryAfter` (`TimeSpan`): Suggested backoff duration before the client may retry.

### Return Value
An immutable `RateLimitLease` instance with `IsAcquired` set to `true` for `Successful` or `false` for `Rejected`.

### Example
```csharp
// Successful lease creation with limit
var okLease = RateLimitLease.Successful(
    remainingPermits: 4, 
    resetTime: DateTimeOffset.UtcNow.AddMinutes(1), 
    limit: 10);

// Rejected lease creation
var rejectedLease = RateLimitLease.Rejected(
    retryAfter: TimeSpan.FromSeconds(15), 
    limit: 10);
```

---

## 3. `CompositeRateLimiter`

### Constructor Signatures
```csharp
public CompositeRateLimiter(IEnumerable<IRateLimiter> limiters);
public CompositeRateLimiter(params IRateLimiter[] limiters);
```

### Properties and Methods
- `IReadOnlyList<IRateLimiter> Limiters { get; }`: Retrieves the immutable collection of ordered child limiters.
- `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)`: Evaluates each limiter sequentially with AND semantics. If any limiter fails or rejects, it performs an immediate rollback by disposing intermediate granted leases.

### Exceptions
- `ArgumentNullException`: When the limiter collection is null.
- `ArgumentException`: When the limiter collection is empty.

### Performance
- Executes deterministically in list order. Place cheaper or stricter limiters (e.g. in-memory burst limiters) earlier in the sequence to avoid unnecessary downstream checks if a request is going to be rejected.

---

## 4. `RateLimiterPolicyBuilder`

### Key Methods
```csharp
public RateLimiterPolicyBuilder AddFixedWindow(string policyName, Action<RateLimiterOptions> configure, TimeProvider? timeProvider = null);
public RateLimiterPolicyBuilder AddSlidingWindow(string policyName, Action<RateLimiterOptions> configure, TimeProvider? timeProvider = null);
public RateLimiterPolicyBuilder AddTokenBucket(string policyName, Action<RateLimiterOptions> configure, TimeProvider? timeProvider = null);
public RateLimiterPolicyBuilder AddConcurrency(string policyName, Action<ConcurrencyRateLimiterOptions> configure);
public RateLimiterPolicyBuilder AddComposite(string policyName, params IRateLimiter[] limiters);
public RateLimiterPolicyBuilder AddPolicy(string policyName, IRateLimiter limiter);
public RateLimiterPolicyBuilder SetDefaultPolicy(IRateLimiter limiter);
public RateLimiterPolicyBuilder SetDefaultPolicy(string policyName);
public IRateLimiterPolicyRegistry Build();
```

### Remarks
Provides a centralized, strongly-typed policy registry during application startup, eliminating magic strings across endpoints.

---

## 5. ASP.NET Core & Minimal API Extensions

### Signatures
```csharp
// Service configuration
public static IServiceCollection AddHttpRateLimiting(this IServiceCollection services, Action<RateLimitingMiddlewareOptions>? configure = null);
public static IServiceCollection AddRateLimiting(this IServiceCollection services, Action<RateLimiterPolicyBuilder> configure, Action<RateLimitingMiddlewareOptions>? configureMiddleware = null);
public static IApplicationBuilder UseHttpRateLimiting(this IApplicationBuilder app);

// Endpoint conventions
public static TBuilder RequireRateLimiting<TBuilder>(this TBuilder builder, string policyName) where TBuilder : IEndpointConventionBuilder;
public static TBuilder DisableRateLimiting<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder;
public static TBuilder RequireDistributedRateLimiting<TBuilder>(this TBuilder builder, string policyName) where TBuilder : IEndpointConventionBuilder;
public static TBuilder DisableDistributedRateLimiting<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder;
```

### Remarks
`RequireDistributedRateLimiting` and `DisableDistributedRateLimiting` are backward-compatible aliases with 100% parity to `RequireRateLimiting` and `DisableRateLimiting`.

---

## 6. Distributed Redis Extensions

### Signatures
```csharp
public static IServiceCollection AddRedisRateLimiting(this IServiceCollection services, Action<RedisRateLimiterOptions> configure);
public static IServiceCollection AddRedisRateLimiting(this IServiceCollection services, IConnectionMultiplexer connectionMultiplexer, Action<RedisRateLimiterOptions>? configure = null);
public static IServiceCollection AddRedisTokenBucketRateLimiting(this IServiceCollection services, Action<RedisTokenBucketRateLimiterOptions> configure);
public static IServiceCollection AddRedisTokenBucketRateLimiting(this IServiceCollection services, IConnectionMultiplexer connectionMultiplexer, Action<RedisTokenBucketRateLimiterOptions>? configure = null);
```

### Remarks
Allows reusing an existing shared `IConnectionMultiplexer` singleton within the host application (avoiding redundant TCP connections to Redis), or allowing the library to manage the multiplexer lifecycle from `RedisRateLimiterOptions.Configuration`.
