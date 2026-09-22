# ADR-003: Named Policies and Endpoint Routing Architecture

## Status
Accepted

## Date
2026-09-04

## Context
In realistic enterprise microservice architectures, a single global rate limit across an entire application is insufficient. Systems require distinct rate limits tailored to specific endpoint profiles:
- Public unauthenticated endpoints (aggressive limits, e.g. 10 req/min).
- Authenticated member endpoints (standard limits, e.g. 100 req/min).
- High-volume data ingestion or export endpoints (token bucket bursts, e.g. 500 tokens).
- Health check and internal webhook endpoints (completely exempt from throttling).

We required a named policy architecture that preserves our core invariants: Native AOT compatibility, zero-allocation leases, and seamless endpoint routing for both Minimal APIs and Controllers.

## Decision
We implement a typed, thread-safe Named Policies and Endpoint Routing subsystem in `EricksonLopez.RateLimiting` and `EricksonLopez.RateLimiting.AspNetCore`.

1. **Policy Registry Abstraction (`IRateLimiterPolicyRegistry`)**:
   - Stores policies in a high-performance, thread-safe `ConcurrentDictionary<string, IRateLimiter>` using case-insensitive ordinal matching. Note: "lock-free" applies to the registry dictionary itself; individual partition implementations (e.g. `SlidingWindowPartition`, `TokenBucketPartition`) use `lock` internally per partition. Only `ConcurrencyPartition` uses lock-free `Interlocked.CompareExchange`.
   - Supports designating a `DefaultLimiter` for requests that do not match an explicit endpoint policy.
   - Provides `IRateLimiter? GetPolicy(string name)` without boxing or reflection.

2. **Fluent Builder (`RateLimiterPolicyBuilder`)**:
   - Enables ergonomic configuration via `services.AddRateLimiting(policies => { ... })`:
     - `.AddFixedWindow(name, configure)`
     - `.AddSlidingWindow(name, configure)`
     - `.AddTokenBucket(name, configure)`
     - `.AddPolicy(name, limiter)`
     - `.SetDefaultPolicy(name | limiter)`

3. **Endpoint Metadata and Conventions**:
   - **Attributes**: `[EnableRateLimiting("policyName")]` and `[DisableRateLimiting]` for MVC controllers and Minimal API route groups. These are custom attributes implementing `IEnableRateLimitingMetadata` and `IDisableRateLimitingMetadata` — they are NOT the BCL `Microsoft.AspNetCore.RateLimiting` attributes.
   - **Conventions**: `.RequireDistributedRateLimiting("policyName")` and `.DisableDistributedRateLimiting()` for Minimal APIs. The `Distributed` prefix prevents naming collisions with BCL's `Microsoft.AspNetCore.Builder.RateLimiterEndpointConventionBuilderExtensions`.

4. **Middleware Resolution Lifecycle**:
   - Step 1: Check endpoint metadata for `IDisableRateLimitingMetadata`. If present, immediately pass through to `_next(context)`.
   - Step 2: Check endpoint metadata for `IEnableRateLimitingMetadata`. If present, lookup policy in `IRateLimiterPolicyRegistry`. If policy is not found, throw a descriptive `InvalidOperationException`.
   - Step 3: If no endpoint policy metadata is present, fallback to `registry.DefaultLimiter` or the ambient `IRateLimiter` registered in DI.
   - Step 4: If no limiter is configured, pass through to `_next(context)`.

## Consequences

### Positive
- **Granular Control**: Enables different rate limiting algorithms and limits per route within the same application.
- **Native AOT Compatible**: Operates strictly through compile-time metadata and dictionary lookups; zero dynamic code generation.
- **Backwards Compatible**: Applications registering a single `IRateLimiter` singleton continue to function without code modifications.

### Negative
- Applications declaring many named policies will hold multiple rate limiter partition dictionaries in memory.

## References
- [ADR-001: Distributed Rate Limiting](./adr-001-distributed-rate-limiting.md)
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
