# ADR-002: Package Existence & Invariant Justification for EricksonLopez.RateLimiting

## Status
Accepted

## Date
2026-09-04

## Context
Under EricksonLopez Design Principles 14 ("Every abstraction must justify its existence") and 15 ("Complexity must be paid for deliberately"), libraries in the ecosystem cannot exist merely as convenience wrappers around third-party or BCL features.

A formal evaluation was conducted to determine whether rate limiting should remain an autonomous Tier 4 package (`EricksonLopez.RateLimiting`), be absorbed into `EricksonLopez.Resilience` or `EricksonLopez.Security`, or be retired in favor of standard BCL `System.Threading.RateLimiting`.

## Decision
We formally confirm `EricksonLopez.RateLimiting` as an autonomous Tier 4 foundational package within the EricksonLopez ecosystem.

Its existence and autonomous boundary are justified by the following non-negotiable invariants:
1. **Single Round-Trip Distributed Atomic Sliding Window**: `RedisSlidingWindowRateLimiter` enforces atomic window pruning, counting, and registration inside a single Redis Lua script, eliminating race conditions across Kubernetes pods.
2. **Zero-Allocation Gateway Evaluation**: `RateLimitLease` is modeled as a `readonly record struct`, producing 0 heap allocations per request check on high-throughput ingress pipelines.
3. **Railway-Oriented Degradation**: All limiter contracts return `Result<RateLimitLease>`, allowing deterministic fail-open or fail-closed handling without unhandled runtime exceptions during Redis infrastructure timeouts. The middleware explicitly processes `Result.IsFailure`, executing `OnRedisFailure` and respecting the configured `FailClosed` policy.
4. **Hierarchical Multi-Tenant Partitioning**: Supports strict identity resolution (`TenantId` -> `SubjectId` -> `IPAddress`) via configurable `PartitionKeyResolver` delegates to isolate noisy neighbors in shared PostgreSQL databases.
5. **Strict Boundary Separation**:
   - Distinct from `EricksonLopez.Resilience` (which governs outbound client resilience via Polly).
   - Distinct from `EricksonLopez.Security` (which governs identity, authentication, and cryptographic integrity).
6. **Zero-Reflection Policy Registry & Observability**: Named policies and OpenTelemetry metrics (`System.Diagnostics.Metrics`) are fully AOT-safe, requiring zero runtime reflection, expression compilation, or dynamic JSON parsing.

## Consequences

### Positive
- Enforces uniform, cluster-wide rate limiting across all enterprise APIs.
- Prevents database connection pool exhaustion caused by runaway clients or scraping bots.
- Guarantees zero GC pressure from lease evaluation in the ASP.NET Core middleware.
- Eliminates unhandled runtime exceptions when caching infrastructure experiences latency spikes.

### Negative
- Requires maintaining the Redis-backed distributed provider (`EricksonLopez.RateLimiting.Redis`) alongside core abstractions.

## References
- [Architectural Justification Document](../architectural-justification.md)
- [ADR-001: Distributed Rate Limiting](./adr-001-distributed-rate-limiting.md)
- [ADR-003: Named Policies & Endpoint Routing Architecture](./adr-003-named-rate-limiting-policies-and-endpoint-routing.md)
- [ADR-004: Resilient Degradation & Middleware Callbacks](./adr-004-resilient-degradation-and-middleware-callbacks.md)
- [ADR-005: OpenTelemetry Metrics & Observability](./adr-005-opentelemetry-metrics-and-observability.md)
