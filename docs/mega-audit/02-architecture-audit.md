# ARCHITECTURAL FORENSIC AUDIT

**Document ID:** AUD-02-ARCH  
**Date:** 2026-09-05  
**Audited Target:** `EricksonLopez.RateLimiting.*` Ecosystem  

---

## 1. Executive Summary

The architectural evaluation of the `EricksonLopez.RateLimiting` ecosystem demonstrates an **exceptionally disciplined design** consistent with Tier 0 infrastructure libraries. The codebase adheres strictly to Clean Architecture, with rigid unidirectional dependency flow from consumer HTTP middleware down to core algorithms and BCL primitives.

However, deep forensic probing revealed **2 architectural edge-case deficiencies** that require formal documentation and evaluation:
1. **[MEDIUM] ARCH-01: In-Memory Partition Cache Duplication:** Rate limiter host classes (`FixedWindowRateLimiter`, `SlidingWindowRateLimiter`, `TokenBucketRateLimiter`, `ConcurrencyRateLimiter`) duplicate the `ConcurrentDictionary` partitioning and `PruneIdlePartitions` eviction loop rather than delegating to an internal partition storage manager.
2. **[HIGH] ARCH-02: Asymmetric Rollback in Composite Chaining:** The `IRateLimiter` abstraction lacks a `Release` or `Refund` verb. When `CompositeRateLimiter` composes single-phase limiters (Fixed Window, Sliding Window, Token Bucket) with multi-phase limiters (Concurrency), a downstream rejection disposes the concurrency slot but permanently burns the permits acquired from earlier windowed/bucket limiters.

---

## 2. Structural Layer Analysis

```text
┌─────────────────────────────────────────────────────────────┐
│  Tier 1: HTTP Presentation                                  │
│  EricksonLopez.RateLimiting.AspNetCore                     │
│  - Responsibilities: Pipeline integration, header          │
│    serialization, route metadata inspection, remote IP      │
│    resolution, HTTP 429/503 body writing.                   │
└──────────────────────────────┬──────────────────────────────┘
                               │ (References Core only)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  Tier 1: Distributed Infrastructure                        │
│  EricksonLopez.RateLimiting.Redis                          │
│  - Responsibilities: StackExchange.Redis multiplexer        │
│    lifecycle, Lua script orchestration, distributed atomic   │
│    ZSET and Hash state transitions, network error mapping.  │
└──────────────────────────────┬──────────────────────────────┘
                               │ (References Core only)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  Tier 0: Core Abstraction & Algorithms                     │
│  EricksonLopez.RateLimiting                                 │
│  - Responsibilities: IRateLimiter contract, struct          │
│    RateLimitLease, in-memory partition algorithms, policy    │
│    registry, fluent builder, OTel metrics.                  │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Detailed Component Isolation Audit

### 3.1 `RateLimiter` vs `RateLimitStore`
- **Assessment:**
  - In distributed systems, rate limiting separates algorithm logic from storage backend.
  - In `EricksonLopez.RateLimiting.Redis`, the algorithm and store are combined into atomic Lua scripts (`SlidingWindowLua`, `TokenBucketLua`) executed directly on Redis. This is a **deliberate and superior architectural decision** because performing `READ -> COMPUTE -> WRITE` across the network would introduce severe race conditions and double allowance under horizontal scaling.
  - For in-memory limiters, partition state is encapsulated inside sealed internal classes (`FixedWindowPartition`, `SlidingWindowPartition`, `TokenBucketPartition`, `ConcurrencyPartition`).

### 3.2 `KeyResolver` & Partitioning Strategy
- **Assessment:**
  - The middleware decouples key extraction via `Func<HttpContext, string> PartitionKeyResolver`.
  - Default resolver resolves `RemoteIpAddress?.ToString() ?? "anonymous"`.
  - Multi-tenant applications can cleanly override this to resolve `tenant_id:user_id` or `api_key`.

### 3.3 `Telemetry` Isolation
- **Assessment:**
  - Metrics are segregated into `RateLimitingMetrics` using standard `System.Diagnostics.Metrics`.
  - No metric creation occurs inside algorithm partitions; metrics are recorded strictly at the limiter wrapper boundary.
  - Structured tags (`limiter.type`, `status`) have strictly bounded cardinality, preventing metrics exhaustion.

---

## 4. SOLID Evaluation Matrix

| Principle | Score | Findings & Analysis |
|---|---|---|
| **Single Responsibility (SRP)** | 9/10 | Classes are hyper-focused. Partition classes do pure math; limiters manage dictionary keys and metric recording; middleware handles HTTP request/response flow. Minor duplication in eviction loops (ARCH-01). |
| **Open/Closed (OCP)** | 10/10 | Extensible via `IRateLimiter`. Third-party algorithms can be added and registered via `RateLimiterPolicyBuilder.AddPolicy()`. |
| **Liskov Substitution (LSP)** | 8/10 | While all implement `AcquireAsync`, the semantic behavior of the returned `RateLimitLease.Dispose()` differs between `ConcurrencyRateLimiter` (active slot release) and windowed/bucket limiters (no-op) (ARCH-02). |
| **Interface Segregation (ISP)** | 10/10 | `IRateLimiter` has exactly 1 method. Metadata interfaces have 0 or 1 property. Zero bloat. |
| **Dependency Inversion (DIP)** | 10/10 | Middleware and consumer APIs depend strictly on `IRateLimiter` and `IRateLimiterPolicyRegistry`. Zero concrete leakage. |

---

## 5. Architectural Anti-Pattern Audit Results

| Anti-Pattern | Result | Evidence |
|---|---|---|
| **God Class / God Method** | PASSED | Highest class line count: 203 lines (`RedisSlidingWindowRateLimiter`). Average method length: 12 lines. |
| **Static State / Mutable Globals** | PASSED | Zero static caches, zero static locks, zero mutable static fields. |
| **Hidden Dependencies** | PASSED | All dependencies (`TimeProvider`, `IOptions`, `ILogger`, `IConnectionMultiplexer`) injected via constructors. |
| **Service Locator** | PASSED (Controlled) | Standard fallback in middleware: `context.RequestServices?.GetService<...>()`. Explicit constructor parameter injection takes precedence. |
| **Temporal Coupling** | PASSED | Single invocation protocol (`AcquireAsync`). No initialization order dependencies. |
| **Infrastructure Leakage** | PASSED | Core project has zero external transport or storage references. |

---

## 6. Architectural Findings & Remediation Plan

### Finding ARCH-01: In-Memory Partition Cache Duplication
- **Severity:** Low / Code Quality
- **Description:** `FixedWindowRateLimiter`, `SlidingWindowRateLimiter`, `TokenBucketRateLimiter`, and `ConcurrencyRateLimiter` each declare their own `ConcurrentDictionary<string, TPartition>` and `PruneIdlePartitions` method.
- **Recommendation:** In a future refactor, introduce an internal `PartitionCache<TPartition>` to centralize max-partition enforcement, thread-safe access, and idle key scavenging.

### Finding ARCH-02: Permanent Permit Burning in Composite Chains
- **Severity:** High / Correctness & Usability
- **Description:** In `CompositeRateLimiter.AcquireAsync`, limiters are evaluated in array order. If limiter 0 (e.g. FixedWindow 100/min) grants 1 permit, and limiter 1 (e.g. TokenBucket 1/sec) rejects the request, `CompositeRateLimiter` calls `Rollback()`. However, `FixedWindowRateLimiter` has already incremented its counter and its `DisposeAction` is `null`. The permit is permanently lost for that client.
- **Root Cause:** `IRateLimiter` lacks a `Refund` or compensating transaction mechanism for non-concurrency rate limiters.
- **Workaround / Architecture Recommendation:** Document that multi-interval composite rate limiters should be ordered from strictest/smallest window to broadest window, or evaluate limiters in read-only pre-flight checks if atomic multi-window support is required.
