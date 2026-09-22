# ADR-006: Concurrency Rate Limiting and Zero-Allocation Lease Disposal

## Status
Accepted

## Date
2026-09-04

## Context
Standard time-based rate limiters (Fixed Window, Sliding Window, Token Bucket) enforce throughput constraints over specific time periods (e.g., requests per second or per minute). However, critical services and database-intensive endpoints frequently require **concurrency limiting** — restricting the maximum number of concurrent in-flight operations active at any given instant.

In the standard .NET BCL (`System.Threading.RateLimiting.ConcurrencyLimiter`), concurrency throttling is implemented using abstract class `RateLimitLease` instances that must be explicitly disposed. This model incurs heap allocations for every lease and provides queueing mechanics that violate our immediate rejection invariant (ADR-001).

We required an in-memory concurrency rate limiter that:
1. Enforces strict limits on concurrent in-flight executions per partition key.
2. Rejects incoming requests immediately when capacity is reached (zero queueing, bounded latency).
3. Preserves zero heap allocation using `readonly record struct RateLimitLease : IDisposable`.
4. Integrates seamlessly into ASP.NET Core middleware pipelines via standard C# `using var lease` scoping.

## Decision
We implement `ConcurrencyRateLimiter` and enhance `RateLimitLease` with deterministic struct-based disposal:

1. **`RateLimitLease` IDisposable Struct Pattern**:
   - `RateLimitLease` implements `IDisposable` directly on the `readonly record struct`.
   - Adds an optional `Action? DisposeAction` callback.
   - For non-concurrency limiters (Sliding Window, Fixed Window, Token Bucket), `DisposeAction` is null and `Dispose()` is an instant, inlined no-op.
   - Struct disposal in C# (`using var lease = ...`) produces **0 bytes of managed heap allocations** and zero boxing overhead.

2. **Atomic In-Memory Concurrency Partition**:
   - `ConcurrencyPartition` manages atomic counter transitions using `Interlocked.CompareExchange` and `Volatile.Read`.
   - When permits are acquired, the active permit count is atomically incremented, and a lease with `() => partition.Release(permits)` is returned.
   - If `current + permits > _limit`, acquisition fails immediately, returning a rejected lease with `RetryAfter = 50ms`.

3. **Automatic Middleware Permit Lifecycle**:
   - `RateLimitingMiddleware` evaluates the lease and assigns `using var lease = leaseResult.Value;`.
   - When the HTTP request pipeline completes execution, `lease.Dispose()` is invoked automatically by the runtime, ensuring that concurrency slots are never leaked even under unhandled exceptions downstream.

4. **Fluent and DI Integration**:
   - Added `AddConcurrencyRateLimiter(...)` extension method in `RateLimitingServiceCollectionExtensions`.
   - Added `.AddConcurrency(name, configure)` to `RateLimiterPolicyBuilder`.

## Consequences

### Positive
- **Deterministic Resource Protection**: Protects compute-heavy endpoints (e.g. report generation, cryptographic operations) from concurrent thread starvation.
- **Zero Allocations Preserved**: Struct-based disposal avoids Gen0 garbage collection churn on high-concurrency gateways.
- **Zero Queuing Invariant Maintained**: Overloaded servers immediately shed excess load (HTTP 429) rather than queuing unbounded requests.

### Negative
- Calls made outside the middleware must explicitly dispose the lease (`using var lease = ...`) to prevent concurrency slot exhaustion.

## References
- [ADR-001: Distributed Rate Limiting](./adr-001-distributed-rate-limiting.md)
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
- [ADR-003: Named Policies & Endpoint Routing](./adr-003-named-rate-limiting-policies-and-endpoint-routing.md)
