# Architectural Justification: EricksonLopez.RateLimiting

## 1. Executive Summary & Ownership Confirmation

`EricksonLopez.RateLimiting` is the **autonomous natural owner of Inbound Traffic Throttling and Distributed Quota Enforcement (Tier 4 — Edge & Infrastructure Protection)** within the EricksonLopez ecosystem.

It is **NOT** a sub-feature of `EricksonLopez.Resilience` (which governs *outbound/internal fault tolerance* via Polly v8 pipelines: circuit breakers, internal retries, and caller throttling) nor a sub-feature of `EricksonLopez.Security` (which governs *authentication, cryptography, zero-trust tokens, and identity*).

This document formally justifies the existence and autonomous status of `EricksonLopez.RateLimiting` in accordance with **EricksonLopez Design Principles**:
- **Principle 14**: *Every abstraction must justify its existence.*
- **Principle 15**: *Complexity must be paid for deliberately.*
- **Invariant-First Doctrine**: A package must not be conceptualized around features ("we have a rate limiter"), but around invariants: *"A distributed multi-tenant API must enforce strict per-tenant and per-user quotas across horizontally scaled nodes atomically, without generating garbage collection overhead, and without failing catastrophically when caching infrastructure drops."*

---

## 2. Core Problem Space & The Failure of BCL Standard Primitives

Standard .NET primitives (`System.Threading.RateLimiting` and `Microsoft.AspNetCore.RateLimiting` introduced in .NET 7+) are structurally incapable of protecting enterprise multi-node deployments out-of-the-box:

```
┌────────────────────────────────────────────────────────────────────────┐
│                   STANDARD BCL LIMITATION PROFILE                      │
├───────────────────────────────┬────────────────────────────────────────┤
│ Flaw in Standard .NET BCL     │ Production Consequence                 │
├───────────────────────────────┼────────────────────────────────────────┤
│ 1. 100% In-Process Only       │ The BCL provides zero distributed      │
│    (No Multi-Node Sync)       │ coordination. In a Kubernetes or Docker│
│                               │ cluster with N pods, a tenant's quota  │
│                               │ is multiplied by N, allowing trivial   │
│                               │ quota evasion via round-robin traffic. │
├───────────────────────────────┼────────────────────────────────────────┤
│ 2. Heap Allocation on Hot Path│ BCL's `RateLimitLease` is an abstract  │
│    (GC Pressure at Gateway)   │ class. Every lease evaluation allocates│
│                               │ memory on Gen 0. At 50,000+ req/sec,   │
│                               │ this induces severe GC pauses.         │
├───────────────────────────────┼────────────────────────────────────────┤
│ 3. Unsafe Race Conditions     │ Ad-hoc distributed limiters use naive   │
│    in Distributed Windows     │ Redis INCR + EXPIRE commands, causing  │
│                               │ window drift and burst-leaking at      │
│                               │ window transition boundaries.          │
├───────────────────────────────┼────────────────────────────────────────┤
│ 4. Exception-Driven Flow      │ Redis network splits or socket timeouts│
│    (Brittle Infrastructure)   │ throw raw RedisExceptions, resulting in│
│                               │ unhandled HTTP 500 errors instead of   │
│                               │ deterministic fallback behavior.       │
├───────────────────────────────┼────────────────────────────────────────┤
│ 5. Naive IP Limiting          │ Simple IP throttling penalizes entire  │
│    (NAT / Corporate Proxy)    │ corporate branches sharing a public    │
│                               │ gateway, rather than throttling the    │
│                               │ specific offending tenant or user.     │
└───────────────────────────────┴────────────────────────────────────────┘
```

---

## 3. Fundamental Architectural Invariants

`EricksonLopez.RateLimiting` enforces five non-negotiable architectural invariants:

### Invariant 1: Single Round-Trip Atomic Distributed Sliding Window
> *All rate limiting evaluations across distributed instances MUST execute atomically within a single Redis Lua script round-trip.*

`RedisSlidingWindowRateLimiter` executes a compiled Lua script operating over Redis Sorted Sets (`ZSET`):
```lua
-- Prune expired timestamps outside sliding window
redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
-- Count active events
local current = redis.call('ZCARD', key)
-- Conditional acquisition and exact retry-after calculation
...
```
- Eliminates multi-step race conditions between application replicas.
- Computes exact mathematical `Retry-After` durations in microseconds based on the oldest event in the active window.

### Invariant 2: Zero-Allocation Lease Evaluation (`readonly record struct`)
> *Evaluating permit acquisition at the edge middleware hot path MUST NOT allocate objects on the managed heap.*

The `RateLimitLease` is modeled as a value type:
```csharp
public readonly record struct RateLimitLease(
    bool IsAcquired,
    int RemainingPermits,
    TimeSpan? RetryAfter = null,
    DateTimeOffset? ResetTime = null,
    Action? DisposeAction = null,
    int? Limit = null) : IDisposable
```
Passed via CPU registers and stack frames, it ensures **0 bytes allocated** per request check, even under 100,000+ requests per second. The optional `DisposeAction` enables deterministic concurrency slot release for `ConcurrencyRateLimiter` without heap allocation — `Dispose()` is an inlined no-op when `DisposeAction` is `null`. The `Limit` property allows downstream middleware to report configured window quotas according to IETF RFC 9651 (`X-RateLimit-Limit`) without requiring separate policy introspection.

### Invariant 3: Railway-Oriented Resilience (`Result<RateLimitLease>`)
> *Storage partition failures (e.g. Redis timeouts) MUST NOT throw unhandled exceptions in the HTTP pipeline.*

The contract returns `Task<Result<RateLimitLease>>`. When an infrastructure fault occurs:
- `RateLimitErrors.ConnectionFailed` is returned functionally.
- The consumer application can configure a deterministic fallback policy: **Fail-Open** (allowing requests while recording telemetry alerts) or **Fail-Closed** (blocking requests under zero-trust posture) without crashing the process.

### Invariant 4: Hierarchical Multi-Tenant Partitioning
> *Rate limiting partition keys MUST resolve according to a deterministic multi-tenant identity hierarchy.*

The partition resolver strictly evaluates identity in order:
$$\text{SubjectId (Authenticated User)} \longrightarrow \text{TenantId / CompanyId (Tenant Organization)} \longrightarrow \text{IPAddress (Anonymous Endpoints)}$$
This guarantees that noisy tenants in multi-tenant shared databases (such as OpusHydra) exhaust only their allotted bandwidth without causing starvation for other tenants.

### Invariant 5: 100% Native AOT & Trim-Safe Compliance
> *No dynamic reflection, code emitting, or unconstrained JSON serialization may be utilized in the rate limiter hot path.*

All configurations, Redis scripting, and options are trim-safe and verified against Native AOT compilation.

---

## 4. Ecosystem Boundary Matrix: RateLimiting vs Resilience vs Security

```
┌────────────────────────────────────────────────────────────────────────┐
│                   ECOSYSTEM CROSS-CUTTING TOPOLOGY                     │
├────────────────────────────────────────────────────────────────────────┤
│ 1. EricksonLopez.Security (Tier 3)                                     │
│    • Responsibility: AuthN, AuthZ, PKCS#11, XML-DSig, ZeroTrust, SAML  │
│    • Direction: Identity & Cryptographic Validity                      │
├────────────────────────────────────────────────────────────────────────┤
│ 2. EricksonLopez.RateLimiting (Tier 4)                                 │
│    • Responsibility: Inbound Traffic Throttling, Distributed Quotas    │
│    • Direction: Edge Protection & Multi-Tenant Capacity Management     │
├────────────────────────────────────────────────────────────────────────┤
│ 3. EricksonLopez.Resilience (Tier 4)                                   │
│    • Responsibility: Outbound / Internal Call Fault Tolerance (Polly)  │
│    • Direction: Circuit Breakers, Outbound Retries, Bulkheads          │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 5. Conclusion

`EricksonLopez.RateLimiting` satisfies all criteria of Principles 14 and 15. It defends mission-critical invariants that standard .NET BCL cannot satisfy, justifying its permanent, autonomous position in Tier 4 of the EricksonLopez platform.

## 6. Associated Architectural Decision Records

- [adr-001: Distributed Rate Limiting](./adr/adr-001-distributed-rate-limiting.md)
- [adr-002: Package Existence & Invariant Justification](./adr/adr-002-package-existence-justification.md)
- [adr-003: Named Policies & Endpoint Routing Architecture](./adr/adr-003-named-rate-limiting-policies-and-endpoint-routing.md)
- [adr-004: Resilient Degradation & Middleware Callbacks](./adr/adr-004-resilient-degradation-and-middleware-callbacks.md)
- [adr-005: OpenTelemetry Metrics & Observability](./adr/adr-005-opentelemetry-metrics-and-observability.md)
- [adr-006: Concurrency Rate Limiting & Zero-Allocation Disposal](./adr/adr-006-concurrency-rate-limiting.md)
- [adr-007: Composite & Multi-Interval Rate Limiting](./adr/adr-007-composite-and-multi-interval-rate-limiting.md)
- [adr-008: Distributed Redis Token Bucket](./adr/adr-008-distributed-redis-token-bucket.md)
- [adr-009: Bounded Partition Lifecycle & Security Hardening](./adr/adr-009-partition-lifecycle-and-security-hardening.md)
