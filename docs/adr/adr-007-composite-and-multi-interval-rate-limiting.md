# ADR-007: Composite and Multi-Interval Rate Limiting Architecture

## Status
Accepted

## Date
2026-09-04

## Context
High-volume production APIs frequently encounter abuse patterns that cannot be addressed by a single rate limit window. For example:
- A service might permit short-lived bursts of **10 requests per second**,
- But must enforce a sustained ceiling of **500 requests per minute**,
- And a daily budget of **10,000 requests per day**.

In standard ASP.NET Core BCL, chaining limiters requires verbose `PartitionedRateLimiter.CreateChained(...)` compositions with complex generic types. In `AspNetCoreRateLimit` (ACRL), multi-interval rules are configured via JSON reflection, which is incompatible with Native AOT compilation.

We required a composite rate limiter that:
1. Evaluates multiple arbitrary `IRateLimiter` instances in sequence using strict AND logic.
2. Accurately computes the most restrictive remaining permits and retry-after periods.
3. Automatically rolls back and disposes acquired permits if a subsequent child limiter rejects or fails.
4. Remains 100% Native AOT compatible with zero dynamic code generation.

## Decision
We implement `CompositeRateLimiter` in `EricksonLopez.RateLimiting`:

1. **Sequential Evaluation with Short-Circuiting**:
   - `CompositeRateLimiter` accepts an ordered collection of `IRateLimiter` instances.
   - Evaluates child limiters sequentially. If any child limiter rejects or returns `Result.Failure`, evaluation short-circuits immediately.

2. **Rollback and Permit Cleanup**:
   - If limiter $N$ rejects or fails after limiters $1 \dots N-1$ succeeded, any previously acquired leases possessing a `DisposeAction` are disposed immediately to prevent resource leakage (critical when combining concurrency limiters with windowed limiters).

3. **Aggregate Lease Metadata**:
   - **`RemainingPermits`**: Evaluated as $\min(\text{RemainingPermits}_1, \dots, \text{RemainingPermits}_N)$.
   - **`RetryAfter`**: When rejected, returns the maximum `RetryAfter` from the rejecting limiter(s).
   - **`ResetTime`**: Evaluated as $\max(\text{ResetTime}_1, \dots, \text{ResetTime}_N)$.
   - **`DisposeAction`**: Combines the disposal callbacks of all acquired child leases into a single aggregate action.

4. **DI and Fluent Builder Support**:
   - `services.AddCompositeRateLimiter(limiter1, limiter2)`
   - `policyBuilder.AddComposite("multi-interval", limiter1, limiter2)`

## Consequences

### Positive
- **Multi-Interval Throttling**: Trivial to configure burst protection alongside sustained quota enforcement.
- **Heterogeneous Composition**: Can combine in-memory concurrency limiting with distributed Redis rate limiting.
- **Rollback Safety**: Prevents leaking in-flight concurrency slots when a downstream rate limiter rejects.
- **Native AOT Compliance**: Built entirely with static types; zero reflection.

### Negative
- Evaluating multiple rate limiters introduces $N$ evaluation steps per request. In distributed Redis setups, chaining multiple Redis limiters increases network round-trips if not combined into a single script.
- **Managed heap allocation per call:** Unlike single-algorithm limiters (which are fully zero-allocation), `CompositeRateLimiter.AcquireAsync` allocates one fixed-size `RateLimitLease[]` array per call (sized to the number of child limiters) to track in-progress leases during sequential evaluation. An additional delegate closure is allocated only when at least one child limiter carries a `DisposeAction` (e.g. when composing with `ConcurrencyRateLimiter`). This is an accepted trade-off: the array sizing is $O(N)$ in child limiter count and eliminates the unbounded growth of a `List<T>`. Applications sensitive to GC pressure on composite hot paths should prefer a small, fixed number of child limiters.

## References
- [ADR-001: Distributed Rate Limiting](./adr-001-distributed-rate-limiting.md)
- [ADR-003: Named Policies & Endpoint Routing](./adr-003-named-rate-limiting-policies-and-endpoint-routing.md)
- [ADR-006: Concurrency Rate Limiting](./adr-006-concurrency-rate-limiting.md)
