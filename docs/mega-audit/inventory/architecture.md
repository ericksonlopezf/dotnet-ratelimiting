# ARCHITECTURAL INVENTORY & STRUCTURAL BOUNDARIES

**Audit Date:** 2026-09-05T04:47:00Z  
**Ecosystem:** `EricksonLopez.RateLimiting`  

---

## 1. Clean Architecture & Boundary Compliance

```text
Layer 3: HTTP Protocol (EricksonLopez.RateLimiting.AspNetCore)
         ├── RateLimitingMiddleware (Pipeline Filter)
         ├── Endpoint Routing Metadata (Declarative Policy Mapping)
         └── HTTP Header Serialization (RFC RateLimit Draft)
                  │
                  ▼
Layer 2: Infrastructure Adapters (EricksonLopez.RateLimiting.Redis)
         ├── StackExchange.Redis Connection Lifecycle
         ├── RedisSlidingWindowRateLimiter (ZSET Lua Script Engine)
         └── RedisTokenBucketRateLimiter (Hash Lua Script Engine)
                  │
                  ▼
Layer 1: Core Domain & Application Engine (EricksonLopez.RateLimiting)
         ├── Contracts & Primitives (IRateLimiter, RateLimitLease)
         ├── In-Memory Sliding Window Partition (Segmented Ring Buffer)
         ├── In-Memory Token Bucket Partition (Continuous Accrual)
         ├── In-Memory Fixed Window Partition (Discrete Epochs)
         ├── In-Memory Concurrency Partition (Lock-Free Interlocked CAS)
         ├── Composite Pipeline Aggregator (AND Evaluation)
         └── Policy Registry & Fluent Builder Subsystem
```

---

## 2. Component Responsibility Isolation Matrix

| Component | Responsibility | Allowed Inbound Dependencies | Forbidden Dependencies | Status |
|---|---|---|---|---|
| `IRateLimiter` | Defines the atomic permit acquisition protocol. | Core, Redis, AspNetCore, Consumers | Anything outside Core | ✅ Compliant |
| `RateLimitLease` | Struct-based immutable result token and resource disposer. | Core, Redis, AspNetCore, Consumers | Classes, Heap allocators | ✅ Compliant |
| `FixedWindowRateLimiter` | Partitioned discrete interval throttling. | In-memory callers, DI | Redis, AspNetCore | ✅ Compliant |
| `SlidingWindowRateLimiter` | Partitioned smooth segmented window throttling. | In-memory callers, DI | Redis, AspNetCore | ✅ Compliant |
| `TokenBucketRateLimiter` | Continuous token replenishment and burst control. | In-memory callers, DI | Redis, AspNetCore | ✅ Compliant |
| `ConcurrencyRateLimiter` | In-flight parallel execution quota enforcement. | In-memory callers, DI | Redis, AspNetCore | ✅ Compliant |
| `CompositeRateLimiter` | Multi-interval AND policy aggregation. | Core, DI | Redis, AspNetCore | ✅ Compliant |
| `RateLimiterPolicyRegistry` | Thread-safe named policy lookup. | Core, AspNetCore | Redis | ✅ Compliant |
| `RateLimitingMiddleware` | Request inspection, partition key resolution, header injection. | ASP.NET Core HTTP host | Redis internal scripts | ✅ Compliant |
| `RedisSlidingWindowRateLimiter` | Horizontally coordinated sliding window over Redis ZSET. | Distributed DI, Host | AspNetCore | ✅ Compliant |
| `RedisTokenBucketRateLimiter` | Horizontally coordinated token bucket over Redis Hash. | Distributed DI, Host | AspNetCore | ✅ Compliant |

---

## 3. SOLID Principles Forensic Audit

### 3.1 Single Responsibility Principle (SRP)
- **Strengths:**
  - Algorithms (`FixedWindowPartition`, `SlidingWindowPartition`, `TokenBucketPartition`, `ConcurrencyPartition`) are isolated from partitioning containers and DI wrappers.
  - Telemetry is segregated into `RateLimitingMetrics` and `Log` without polluting algorithm arithmetic.
  - HTTP middleware delegates permit decisions strictly to `IRateLimiter`.
- **Finding ARCH-01 (Minor SRP Overlap):**
  - In-memory rate limiters (`FixedWindowRateLimiter`, `SlidingWindowRateLimiter`, `TokenBucketRateLimiter`, `ConcurrencyRateLimiter`) currently manage both algorithm execution AND concurrent partition cache management (`ConcurrentDictionary<string, Partition>` + `PruneIdlePartitions`). Extracting a shared `PartitionCache<TPartition>` would reduce duplication across the 4 limiters.

### 3.2 Open/Closed Principle (OCP)
- **Strengths:**
  - `IRateLimiter` allows arbitrary custom rate limiting strategies (e.g. database-backed, distributed Consul, local leaky bucket) without altering core or middleware.
  - `CompositeRateLimiter` allows unlimited composition of child limiters without modifying their internals.
  - `RateLimiterPolicyBuilder.AddPolicy(name, limiter)` allows third-party rate limiters to be registered into the named policy registry.

### 3.3 Liskov Substitution Principle (LSP)
- **Finding ARCH-02 (Token Refund Disparity in Composite Composition):**
  - `ConcurrencyRateLimiter` fulfills a two-phase acquisition model: acquire permit -> return lease with `DisposeAction` -> release permit upon lease disposal.
  - All other limiters (`FixedWindow`, `SlidingWindow`, `TokenBucket`, `Redis*`) are single-phase consumption models: acquire permit -> permit consumed permanently, `DisposeAction = null`.
  - When composed in `CompositeRateLimiter`, if limiter 1 consumes a token and limiter 2 rejects the request, `CompositeRateLimiter` calls `Rollback()`. For `ConcurrencyRateLimiter`, the slot is released. For windowed/token-bucket limiters, `Dispose()` is a no-op, causing silent permanent permit loss.

### 3.4 Interface Segregation Principle (ISP)
- **Strengths:**
  - `IRateLimiter` exposes a single atomic method: `AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)`. No bloated admin or reset methods on the hot-path interface.
  - AspNetCore metadata is segregated into two zero-method or single-property interfaces: `IEnableRateLimitingMetadata` and `IDisableRateLimitingMetadata`.

### 3.5 Dependency Inversion Principle (DIP)
- High-level modules (`RateLimitingMiddleware`) depend exclusively on the abstraction `IRateLimiter`, never on concrete classes like `RedisSlidingWindowRateLimiter` or `SlidingWindowRateLimiter`.
- `RateLimiterPolicyRegistry` holds `IRateLimiter` references.

---

## 4. Architectural Anti-Pattern Detection

| Anti-Pattern | Status in Codebase | Evidence / Analysis |
|---|---|---|
| **God Class** | ❌ None Detected | Max file length is ~200 lines (`RedisSlidingWindowRateLimiter.cs` has 203 lines). All classes are concise. |
| **God Method** | ❌ None Detected | Longest method is `RateLimitingMiddleware.InvokeAsync` (110 lines) handling endpoint routing, fallback, lease check, headers, and 429 writing. |
| **Static State / Mutable Globals** | ❌ None Detected | No static collections, counters, or locks. Static members are limited to `RateLimitingMetrics` (OTel meter/counters) and `RateLimitingHeaders` (constants). |
| **Hidden Dependencies** | ❌ None Detected | All dependencies are explicitly injected via constructors (`IOptions<T>`, `ILogger<T>`, `IConnectionMultiplexer`, `TimeProvider`). |
| **Service Locator** | ⚠️ Controlled Fallback | In `RateLimitingMiddleware.InvokeAsync`, `context.RequestServices?.GetService<...>()` is used as a fallback if parameters are not injected via middleware invocation. This is standard ASP.NET Core middleware idiom, but explicit DI injection is preferred. |
| **Temporal Coupling** | ❌ None Detected | `AcquireAsync` is a self-contained atomic operation. No mandatory pre-initialization or post-state check. |
| **Infrastructure Leakage** | ❌ None Detected | Core assembly has zero references to Redis or ASP.NET Core. |
