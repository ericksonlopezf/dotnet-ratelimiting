# TRACEABILITY MATRIX & VERIFICATION MAPPING

**Document ID:** AUD-28-TRACE  
**Date:** 2026-09-05  
**Audited Target:** Requirements to Code to Test to Finding Traceability  

---

## 1. Full Lifecycle Traceability Matrix

| Requirement / Invariant | Implementation File | Verification Test | Attack / Stress Harness | Benchmark Target | Evidence File | Finding Reference | Current Status |
|---|---|---|---|---|---|---|---|
| **REQ-01: Fixed Window Throttling** | `FixedWindowRateLimiter.cs`, `FixedWindowPartition.cs` | `FixedWindowRateLimiterTests` | `1024-Thread Saturated Burst` | `EricksonLopez_FixedWindow_AcquireAsync` | `evidence/concurrency/fixed-window-stress.md` | N/A | ✅ CERTIFIED |
| **REQ-02: Sliding Window Smoothing**| `SlidingWindowRateLimiter.cs`, `SlidingWindowPartition.cs`| `SlidingWindowRateLimiterTests`| `Window Boundary Burst Bypass Attack` | `EricksonLopez_SlidingWindow_AcquireAsync` | `evidence/concurrency/sliding-window-stress.md` | N/A | ✅ CERTIFIED |
| **REQ-03: Continuous Token Refill** | `TokenBucketRateLimiter.cs`, `TokenBucketPartition.cs` | `TokenBucketRateLimiterTests` | `NTP Clock Jump Backwards (-1 hour)` | `EricksonLopez_TokenBucket_AcquireAsync` | `evidence/security/clock-retrocession.md` | N/A | ✅ CERTIFIED |
| **REQ-04: Concurrency Throttling** | `ConcurrencyRateLimiter.cs`, `ConcurrencyPartition.cs` | `ConcurrencyRateLimiterTests` | `Concurrent Acquire vs Prune Eviction`| Microsecond CAS loop | `evidence/concurrency/cas-stress.md` | N/A | ✅ CERTIFIED |
| **REQ-05: Multi-Interval AND Chaining**| `CompositeRateLimiter.cs` | `CompositeRateLimiterTests` | `Cancellation Mid-Flight Leak Attack` | Chained Pipeline | `evidence/security/cancellation-leak.md` | `FINDING-ARCH-02`, `FINDING-MUT-01` | ⚠️ DOCUMENTED |
| **REQ-06: Distributed Redis Coordination**| `RedisSlidingWindowRateLimiter.cs`| `RedisSlidingWindowRateLimiterTests`| `Distributed Multi-Node Race Simulation`| Network Lua Round-Trip | `evidence/distributed/redis-lua.md` | `FINDING-REDIS-01` | ✅ CERTIFIED |
| **REQ-07: Distributed Token Bucket** | `RedisTokenBucketRateLimiter.cs` | `RedisTokenBucketRateLimiterTests` | `Simulated Socket Exception Crash` | Network Lua Round-Trip | `evidence/distributed/redis-tb.md` | N/A | ✅ CERTIFIED |
| **REQ-08: Fail-Open / Fail-Closed** | `RateLimitingMiddleware.cs` | `AspNetCoreAdversarialTests` | `Hard Redis Disconnect Attack` | Downstream Pipeline Hop | `evidence/chaos/fail-open-closed.md` | N/A | ✅ CERTIFIED |
| **REQ-09: HTTP 429 & Retry-After** | `RateLimitingMiddleware.cs` | `RateLimitingMiddlewareTests` | `Sub-second Fractional Rejection` | Header Formatting | `evidence/security/http-headers.md` | N/A | ✅ CERTIFIED |
| **REQ-10: Response Started Guard** | `RateLimitingMiddleware.cs` | `MegaAuditAspNetCoreAdversarialSuite` | `Streaming Response Header Flush` | Async Pipeline | `evidence/security/response-started.md`| N/A | ✅ CERTIFIED |
| **REQ-11: OpenTelemetry Metrics** | `RateLimitingMetrics.cs` | `MetricsTests` | `Cardinality Explosion Flooding Attack` | In-Memory Counter | `evidence/performance/metric-cardinality.md`| N/A | ✅ CERTIFIED |
| **REQ-12: Native AOT Compatibility** | `Directory.Build.props`, All .csproj | Native AOT Compiler Analyzer | `IL Linker Trim Scan` | PublishAOT net10.0 | `evidence/performance/aot-verification.md`| N/A | ✅ CERTIFIED |
| **REQ-13: Memory Ceiling Boundary** | All in-memory limiters | `AdversarialRegressionTests` | `100,000 Unique Key Flooding Attack` | GC Memory Profiler | `evidence/dos/partition-saturation.md` | `FINDING-DOS-01` | ⚠️ CONDITIONALLY READY |
| **REQ-14: Zero-Allocation Fast Path**| `RateLimitLease.cs` | `RateLimitLeaseTests` | `Struct Boxing & Finalizer Probing` | `BenchmarkDotNet Gen0` | `evidence/performance/allocation-profile.md`| `FINDING-PERF-01` | ⚠️ DOCUMENTED (72B Task) |
| **REQ-15: Reverse Proxy IP Resolution**| `RateLimitingMiddlewareOptions.cs`| `RateLimitingMiddlewareTests` | `X-Forwarded-For Spoofing Attack` | In-Memory Dictionary | `evidence/security/header-spoofing.md` | `FINDING-SEC-01` | ⚠️ DOCUMENTED |

---

## 2. Finding-to-Regression Traceability

```text
Finding DOS-01 (Partition Saturation)
└── Code: FixedWindowRateLimiter.cs:40-50
    └── Evidence: evidence/dos/partition-saturation.md
        └── Attack: attacks/dos/partition-saturation-poc.md
            └── Mitigation: PartitionCache LRU Roadmap (v1.3.0)
                └── Regression Test: tests/regression/AdversarialRegressionTests.cs

Finding ARCH-02 (Composite Token Burning)
└── Code: CompositeRateLimiter.cs:84-88
    └── Evidence: evidence/security/cancellation-leak.md
        └── Attack: attacks/bypass/composite-permit-drain.md
            └── Mitigation: Strictest-First Window Ordering Guidance
                └── Regression Test: tests/regression/CompositePermitBurnTests.cs

Finding PERF-01 (Task 72-byte Allocation)
└── Code: IRateLimiter.cs:29
    └── Evidence: evidence/performance/allocation-profile.md
        └── Benchmark: benchmarks/baseline/hot-path-baseline.md
            └── Mitigation: ValueTask Migration Roadmap (v2.0.0)
                └── Regression Test: RateLimiterHotPathBenchmarks.cs
```
