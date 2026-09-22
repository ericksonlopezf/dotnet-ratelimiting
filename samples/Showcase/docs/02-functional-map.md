# Phase 2 · Functional Map and Flow Architecture

This document describes the complete functional interaction of the components in `EricksonLopez.RateLimiting` based on the actual architecture discovered in the source code.

---

## 1. Ecosystem Architectural Layer Model

The system operates under a reactive, deterministic pipeline that processes permit acquisitions in real time, both in standalone (in-memory) and distributed (Redis) modes.

```
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │                         1. INGRESS LAYER                                    │
 │  • HTTP Request (ASP.NET Core Endpoint / MVC Controller / Minimal API)      │
 │  • Direct C# Invocation (Background Service, Job Scheduler, Worker Service) │
 └──────────────────────────────────────┬──────────────────────────────────────┘
                                        │
                                        ▼
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │                       2. PROCESSING LAYER                                   │
 │  • RateLimitingMiddleware: Endpoint Metadata Detection                      │
 │    - Is IDisableRateLimitingMetadata present? ──► Immediate Bypass          │
 │    - Is IEnableRateLimitingMetadata present?  ──► Policy Resolution         │
 │  • PartitionKeyResolver: Key Resolution (Tenant / User / IP)                │
 │  • IRateLimiterPolicyRegistry: IRateLimiter Selection (Named/Default)       │
 └──────────────────────────────────────┬──────────────────────────────────────┘
                                        │
                                        ▼
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │                   3. PERSISTENCE & EVALUATION LAYER                         │
 │                                                                             │
 │  ┌───────────────────────────────┐     ┌──────────────────────────────────┐ │
 │  │      IN-MEMORY (O(1))         │     │       DISTRIBUTED (Redis)        │ │
 │  │ • FixedWindowRateLimiter      │     │ • RedisSlidingWindowRateLimiter  │ │
 │  │ • SlidingWindowRateLimiter    │     │   (Atomic Lua script / ZSET)     │ │
 │  │ • TokenBucketRateLimiter      │     │ • RedisTokenBucketRateLimiter    │ │
 │  │ • ConcurrencyRateLimiter      │     │   (Atomic Lua script / Hash)     │ │
 │  │ • CompositeRateLimiter (AND)  │     │                                  │ │
 │  │ ConcurrentDictionary Partition│     │ Networks, Timestamps, Serializ.  │ │
 │  └───────────────┬───────────────┘     └────────────────┬─────────────────┘ │
 └──────────────────┼──────────────────────────────────────┼───────────────────┘
                    └───────────────────┬──────────────────┘
                                        │
                                        ▼
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │                     4. DISPATCH & DECISION LAYER                            │
 │  • Result<RateLimitLease> Emission:                                         │
 │    - IsAcquired = true: RemainingPermits, ResetTime, Limit, DisposeAction?  │
 │    - IsAcquired = false: RetryAfter, ResetTime, Limit                       │
 │    - Result.IsFailure: Error.Code (e.g. RateLimit.Redis.ConnectionFailed)   │
 └──────────────────────────────────────┬──────────────────────────────────────┘
                                        │
                                        ▼
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │                    5. RESPONSE & COMMIT LAYER                               │
 │  • HTTP Header Injection:                                                   │
 │    - X-RateLimit-Limit                                                      │
 │    - X-RateLimit-Remaining                                                  │
 │    - X-RateLimit-Reset (Unix Timestamp seconds)                             │
 │  • If IsAcquired == true:                                                   │
 │    - Invokes downstream pipeline (`_next(context)`)                         │
 │  • If IsAcquired == false:                                                  │
 │    - Header: Retry-After (seconds)                                          │
 │    - Is OnRejected defined? ──► Custom Callback (e.g. ProblemDetails)       │
 │    - Otherwise ──────────────► Standard JSON HTTP 429 Too Many Requests     │
 │  • If Result.IsFailure:                                                     │
 │    - Is OnRedisFailure defined? ──► Custom Resilience Callback              │
 │    - FailClosed == true?       ──► HTTP 503 Service Unavailable             │
 │    - FailClosed == false?      ──► Fail-Open: Invokes `_next(context)`      │
 └──────────────────────────────────────┬──────────────────────────────────────┘
                                        │
                                        ▼
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │                    6. CLEANUP & LIFECYCLE LAYER                             │
 │  • In-Memory: Automatic pruning of idle partitions (> MaxPartitions)        │
 │  • Concurrency: Invocation of `lease.Dispose()` freeing concurrency slots   │
 │  • Redis: Natural key expiration via `PEXPIRE` configured in Lua scripts    │
 │  • Composite: Automatic rollback of leases upon intermediate failures       │
 └──────────────────────────────────────┬──────────────────────────────────────┘
                                        │
                                        ▼
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │                     7. TELEMETRY & OBSERVABILITY                            │
 │  • RateLimitingMetrics.RecordRequest:                                       │
 │    - Counter: `rate_limit.requests.total` (Tags: limiter.type, status)      │
 │    - Histogram: `rate_limit.lease.duration` (ms)                            │
 └─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Detailed Transition Descriptions Between Layers

### Transition 1: Ingress ➔ Processing
- When an HTTP request enters ASP.NET Core, the pipeline executes `RateLimitingMiddleware`.
- The middleware inspects the endpoint metadata (`context.GetEndpoint()`).
- If it encounters `IDisableRateLimitingMetadata` (applied via `.DisableRateLimiting()` or `[DisableRateLimiting]`), the request bypasses rate limiting and proceeds directly to `_next(context)` without evaluating quotas or allocating resources.
- If it encounters `IEnableRateLimitingMetadata` (applied via `.RequireRateLimiting("tier")` or `[EnableRateLimiting("tier")]`), it extracts the `PolicyName` and looks it up in `IRateLimiterPolicyRegistry`. If the policy does not exist, it immediately throws `InvalidOperationException` to prevent silent misconfigurations.
- If no endpoint-specific metadata exists, the middleware falls back to `policyRegistry.DefaultLimiter` or the default `IRateLimiter` registered in DI.

### Transition 2: Processing ➔ Persistence Evaluation
- `PartitionKeyResolver(context)` is invoked. By default, it resolves the remote client IP address (`context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"`).
- It calls `rateLimiter.AcquireAsync(key, options.PermitCost, context.RequestAborted)`.
- For in-memory implementations:
  - The partition is retrieved or created in `ConcurrentDictionary`.
  - If partition count exceeds `MaxPartitions`, deterministic idle partition pruning (`PruneIdlePartitions`) runs.
  - The operation is synchronous, zero-allocation, ultra-fast ($O(1)$), and returned via `Task.FromResult`.
- For distributed Redis implementations:
  - The current timestamp in microseconds is computed, and an atomic `ScriptEvaluateAsync` command is dispatched to Redis.
  - The Lua script evaluates all conditions and state mutations atomically on the Redis server in a single round-trip, eliminating distributed race conditions.

### Transition 3: Evaluation ➔ Dispatch & Response
- The limiter returns a `Result<RateLimitLease>`.
- If `Result.IsFailure`:
  - The failure is recorded in `RateLimitingMetrics` with status `"failed"`.
  - It checks `options.OnRedisFailure`. If defined, the custom callback runs (allowing diagnostics or header injections like `X-RateLimit-Degraded`).
  - If no callback is defined, `options.FailClosed` is evaluated:
    - `FailClosed = true`: Returns HTTP 503 Service Unavailable with error details.
    - `FailClosed = false` (Fail-Open): Allows the request to continue downstream (`_next(context)`), preserving application availability during Redis degradation.
- If `Result.IsSuccess`:
  - Telemetry is recorded with status `"acquired"` or `"rejected"`.
  - Standard headers `X-RateLimit-Limit`, `X-RateLimit-Remaining`, and `X-RateLimit-Reset` are injected into the HTTP response.
  - If `lease.IsAcquired == true`: Execution proceeds downstream to `_next(context)`.
  - If `lease.IsAcquired == false`: The `Retry-After` header is injected. If `options.OnRejected` is provided, it is invoked to generate custom formats (e.g. RFC 7807 ProblemDetails); otherwise, a standard HTTP 429 Too Many Requests JSON payload is returned.

### Transition 4: Cleanup & Lifecycle Management
- For concurrency limiters (`ConcurrencyRateLimiter`), the lease returns a `DisposeAction` encapsulated in `OneShotDisposer`. When leaving the `using var lease` block, atomic slots are immediately decremented via `Interlocked.Exchange`, freeing capacity for pending requests.
- For composite limiters (`CompositeRateLimiter`), if any child limiter rejects or fails during sequence evaluation, `Rollback(acquiredLeases)` runs immediately, disposing all previously acquired leases to prevent partial, inconsistent quota consumption.
