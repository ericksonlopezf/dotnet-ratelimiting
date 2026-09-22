# AUDIT FINDINGS CATALOG — FORENSIC REPORT

**Document ID:** AUD-26-FINDINGS  
**Date:** 2026-09-05  
**Audited Target:** `EricksonLopez.RateLimiting.*` Ecosystem  

---

## Finding FINDING-DOS-01: Noisy Neighbor Partition Saturation Denial of Service

- **ID:** `FINDING-DOS-01`
- **Title:** Partition Saturation Denial of Service Under Malicious High-Cardinality Key Flooding
- **Severity:** **HIGH**
- **CWE:** `CWE-400: Uncontrolled Resource Consumption`
- **CVSS v3.1:** `7.5 (CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:H)`
- **Location:** 
  - `FixedWindowRateLimiter.cs:40-50`
  - `SlidingWindowRateLimiter.cs:40-50`
  - `TokenBucketRateLimiter.cs:38-48`
  - `ConcurrencyRateLimiter.cs:40-53`
- **Description:** When distinct keys reach `MaxPartitions` (default 10,000), the rate limiter invokes `PruneIdlePartitions()`. If all 10,000 partitions have received requests during the current window, none are idle. When the 10,001-st request arrives (even from a legitimate user with full quota), the limiter fails closed and rejects the request with HTTP 429.
- **Attack Scenario:** An attacker sends 10,000 requests with spoofed client identifiers (`bot-1` to `bot-10000`) within 10 seconds. All subsequent requests from legitimate users arriving in that 1-minute window are rejected, taking down the application for new clients.
- **Reproduction:** Call `AcquireAsync` with 10,000 distinct keys in the same second, then call `AcquireAsync` with key 10,001. Lease is rejected.
- **Impact:** Complete service denial for legitimate users under modest traffic flooding.
- **Likelihood:** High for public-facing unauthenticated APIs.
- **Evidence:** Executable trace in Chapter 08 of the audit.
- **Root Cause:** Expiration-based eviction policy instead of capacity-based LRU eviction when capacity is reached.
- **Recommendation:** Implement an LRU eviction strategy in `PartitionCache` when `_partitions.Count >= MaxPartitions`, or document using the Redis distributed rate limiter for high-cardinality environments.
- **Regression Test:** `AdversarialRegressionTests.PartitionSaturation_UnderHighKeyFlooding`.

---

## Finding FINDING-ARCH-02: Permanent Token Burning in Composite Chains

- **ID:** `FINDING-ARCH-02`
- **Title:** Permanent Permit Loss Upon Downstream Rejection in `CompositeRateLimiter`
- **Severity:** **HIGH**
- **CWE:** `CWE-770: Allocation of Resources Without Limits or Throttling`
- **CVSS v3.1:** `6.5 (CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:L/A:L)`
- **Location:** `CompositeRateLimiter.cs:84-88, 148-155`
- **Description:** `CompositeRateLimiter` evaluates child limiters in array order. If limiter 0 (e.g. FixedWindow 100/min) grants 1 permit, and limiter 1 (e.g. TokenBucket 1/sec) rejects the request, `CompositeRateLimiter` invokes `Rollback()`. For windowed and bucket limiters, `DisposeAction` is null, so no refund occurs. The permit consumed from limiter 0 is permanently lost.
- **Attack Scenario:** A client sends rapid bursts. Limiter 1 (burst limiter) rejects, but each rejected attempt drains the client's sustained hourly quota in Limiter 0, prematurely exhausting their daily budget without serving any successful requests.
- **Reproduction:** Configure composite limiter with (FixedWindow limit: 10/min, TokenBucket limit: 1/sec). Send 2 rapid requests. Request 2 is rejected by TokenBucket, but FixedWindow remaining drops to 8!
- **Impact:** Client quotas are depleted by rejected requests.
- **Likelihood:** Medium to High in multi-interval rate limiting configurations.
- **Evidence:** Source code analysis of `CompositeRateLimiter.Rollback()`.
- **Root Cause:** Single-phase consumption model in `IRateLimiter` lacks a compensating transaction (`Refund`) interface.
- **Recommendation:** Document that composite limiters must order child limiters from smallest window to largest window, or implement pre-flight verification.
- **Regression Test:** `CompositeRateLimiterTests.Rollback_WithWindowedLimiters_DocumentsPermitBurnBehavior`.

---

## Finding FINDING-PERF-01: `Task` vs `ValueTask` 72-Byte Heap Allocation

- **ID:** `FINDING-PERF-01`
- **Title:** Unavoidable 72-Byte `Task` Heap Allocation on Synchronous Fast Path
- **Severity:** **MEDIUM**
- **CWE:** `CWE-400: Uncontrolled Resource Consumption`
- **CVSS v3.1:** `4.3 (CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:L)`
- **Location:** `IRateLimiter.cs:29`, `FixedWindowRateLimiter.cs:59`, `SlidingWindowRateLimiter.cs:59`, `TokenBucketRateLimiter.cs:58`, `ConcurrencyRateLimiter.cs:79`
- **Description:** Documentation claims "zero heap allocations per lease check". However, `IRateLimiter.AcquireAsync` returns `Task<Result<RateLimitLease>>`. Because `Result<RateLimitLease>` is a custom composite struct, `Task.FromResult` allocates a 72-byte `Task<T>` object on the managed heap on every synchronous call.
- **Impact:** At 100,000 RPS, generates 7.2 MB/sec of Gen0 garbage, increasing GC pause frequency.
- **Likelihood:** 100% on every call to in-memory limiters.
- **Evidence:** BenchmarkDotNet allocation measurements (72 B / op).
- **Root Cause:** Unified interface uses `Task<T>` to accommodate asynchronous Redis operations.
- **Recommendation:** Migrate `IRateLimiter.AcquireAsync` to return `ValueTask<Result<RateLimitLease>>` in the next milestone.
- **Regression Test:** `RateLimiterHotPathBenchmarks`.

---

## Finding FINDING-SEC-01: Reverse Proxy RemoteIpAddress Blindness

- **ID:** `FINDING-SEC-01`
- **Title:** Global Bucket Throttling Behind Reverse Proxies Without Forwarded Headers
- **Severity:** **MEDIUM**
- **CWE:** `CWE-350: Reliance on Reverse DNS / IP Address`
- **CVSS v3.1:** `5.3 (CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:L/A:N)`
- **Location:** `RateLimitingMiddlewareOptions.cs:19-20`
- **Description:** The default key resolver resolves `context.Connection.RemoteIpAddress`. In containerized environments behind ingress proxies, this is the proxy's IP. All clients share the same bucket unless `UseForwardedHeaders()` is configured.
- **Impact:** One client can exhaust the rate limit for all users behind the same ingress controller.
- **Likelihood:** High in Kubernetes / AWS ALB deployments if documentation is unread.
- **Root Cause:** ASP.NET Core architectural design.
- **Recommendation:** Add explicit callouts in README and sample projects.
- **Regression Test:** `RateLimitingMiddlewareTests.DefaultPartitionKeyResolver_ResolvesRemoteIpOrAnonymous`.

---

## Finding FINDING-REDIS-01: Full ZSET Memory Retrieval on Rejection in Lua Script

- **ID:** `FINDING-REDIS-01`
- **Title:** `ZRANGE 0 -1 WITHSCORES` Loads Entire Sorted Set Into Redis Lua Memory
- **Severity:** **LOW**
- **CWE:** `CWE-400: Uncontrolled Resource Consumption`
- **CVSS v3.1:** `3.1 (CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:N/I:N/A:L)`
- **Location:** `RedisSlidingWindowRateLimiter.cs:71`
- **Description:** When a request is rejected, the Lua script calls `redis.call('ZRANGE', key, 0, -1, 'WITHSCORES')`, fetching all members of the ZSET into Lua memory to compute `Retry-After`.
- **Impact:** Minor Redis CPU and memory overhead when rejecting requests on keys with high quotas ($> 50,000$).
- **Likelihood:** Low (only occurs during rejections on very high quota keys).
- **Root Cause:** Convenience of fetching full range instead of bounded slice.
- **Recommendation:** Query only `0 target_idx` instead of `0 -1`.
- **Regression Test:** `RedisSlidingWindowRateLimiterTests.SlidingWindow_ShouldReject_WhenLimitExceeded`.

---

## Finding FINDING-MUT-01: Missing Assertion for Fallback Retry-After in Composite Limiter

- **ID:** `FINDING-MUT-01`
- **Title:** Missing Test Assertion for Default `RetryAfter` Fallback in `CompositeRateLimiter`
- **Severity:** **LOW**
- **CWE:** `CWE-1077: Floating Point Comparison / Test Gap`
- **CVSS v3.1:** `0.0 (Internal Test Quality)`
- **Location:** `CompositeRateLimiter.cs:91`
- **Description:** Stryker mutation survival: mutating `lease.RetryAfter ?? TimeSpan.FromSeconds(1)` to `TimeSpan.FromSeconds(5)` survives because tests do not assert the fallback value when child limiter returns null `RetryAfter`.
- **Impact:** Test gap only.
- **Recommendation:** Add test verifying fallback default of 1 second.
- **Regression Test:** `CompositeRateLimiterTests.AcquireAsync_ChildRejectsWithNullRetryAfter_DefaultsToOneSecond`.

---

## Finding FINDING-ARCH-01: In-Memory Partition Boilerplate Duplication

- **ID:** `FINDING-ARCH-01`
- **Title:** Partition Dictionary and Eviction Code Duplication Across In-Memory Limiters
- **Severity:** **INFO**
- **CWE:** `CWE-1041: Redundant Code`
- **Location:** Core limiter classes
- **Description:** `ConcurrentDictionary` management and `PruneIdlePartitions` are repeated across all 4 in-memory limiters.
- **Recommendation:** Extract internal `PartitionCache<TPartition>`.

---

## Finding FINDING-API-01: Legacy Method Aliases in Endpoint Extensions

- **ID:** `FINDING-API-01`
- **Title:** Redundant Legacy Aliases `RequireDistributedRateLimiting` in Public API
- **Severity:** **INFO**
- **CWE:** `N/A`
- **Location:** `EndpointRateLimitingExtensions.cs:50, 61`
- **Description:** Redundant aliases for `RequireRateLimiting` and `DisableRateLimiting`.
- **Recommendation:** Retain for v1.x, deprecate in v2.0.
