<!-- Copyright © Erickson Lopez. MIT License. -->
# Product Strategy — EricksonLopez.RateLimiting
## From Feature Matrix to Competitive Ecosystem Strategy

> **Date:** 2026-09-04 (v1.0.0 GA Governance)  
> **Source Baseline:** Competitive Functional Parity Audit  
> **Analytical Scope:** Product Architecture & Developer Ecosystem Strategy  

---

## 1. Product Context & Value Proposition

### Problem Definition
`EricksonLopez.RateLimiting` protects inbound .NET APIs against abuse, traffic spikes, and quota exhaustion through partition-keyed throttling. It is built specifically for **platform engineers and backend architects** operating high-load APIs in distributed containerized environments (.NET 8, .NET 9, .NET 10, Kubernetes, Native AOT).

### Target Audience

**Primary Profile:** Senior Backend / Cloud Platform Engineer in .NET operating high-throughput APIs requiring:
- Multi-tenancy (isolated limits per tenant/organization).
- High request velocity (>10,000 req/s where GC pressure impacts p99 latency).
- Distributed topology (multiple pods/instances sharing state).
- Resilient infrastructure failure handling (controlled Fail-Open on Redis drops).
- Native AOT deployment readiness (zero reflection/dynamic code generation).

**Secondary Profile:** Enterprise architects standardizing on the `EricksonLopez.*` library family for cohesive API contracts (`Result<T>`, `IRateLimiter`).

**Explicitly Out of Scope:**
- Simple monolithic apps without distributed or multi-tenant needs (standard BCL is sufficient).
- Request queuing pipelines (BCL queuing is preferred when client wait queues are desired).

---

## 2. Core Strategic Differentiators

1. **Deterministic Failure Degradation**: When Redis experiences network partitions or timeouts, standard libraries crash requests with unhandled exceptions. `EricksonLopez.RateLimiting` treats infrastructure outages as typed `Result.Failure(Error)` values, providing configurable zero-downtime Fail-Open or Fail-Closed fallback.
2. **True Hot-Path Zero Allocation**: Struct-based leases (`RateLimitLease`) eliminate garbage collection allocations on every inbound check.
3. **Certified Native AOT & Trimming**: Clean Native AOT compliance without dynamic dependencies or IL warnings.
4. **TimeProvider Testability**: Mockable time provider enables deterministic unit testing without wall-clock sleep delays.
5. **Fluent Policy Composition**: Cascade multi-window rate limits (e.g. 10 req/s AND 100 req/min) with atomic rollback.

---

## 3. Ecosystem Positioning & Roadmap

`EricksonLopez.RateLimiting` occupies the **Inbound Edge & Infrastructure Protection Tier** within the EricksonLopez ecosystem. It composes cleanly with:
- `EricksonLopez.MultiTenancy`: Supplying tenant-resolved partition keys.
- `EricksonLopez.Security`: Supplying authenticated subject identities.
- `EricksonLopez.Result`: Providing uniform railway error propagation.
- `EricksonLopez.Concurrency`: Providing process-level thread synchronization primitives.
