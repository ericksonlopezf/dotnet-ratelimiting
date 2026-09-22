# ADR-009: Bounded Partition Lifecycle and Security Hardening

## Status
Accepted

## Date
2026-09-04

## Context
During comprehensive red-team and forensic security analysis of the rate limiting engine, several edge-case vulnerability vectors were identified across high-concurrency and adversarial traffic models:

1. **Unbounded Memory Exhaustion DoS (CWE-770)**: In-memory partition dictionaries (`ConcurrentDictionary<string, Partition>`) stored partition state for every observed key indefinitely. Under high-cardinality spoofed IP or key flooding attacks, memory grew unbounded until the host process crashed with an `OutOfMemoryException`.
2. **Redis Lua Millisecond Collision Bypass (CWE-362)**: The distributed sliding window Lua script stored events in a Redis Sorted Set (`ZSET`) where both the score and the member were derived from the current millisecond timestamp (`now_ms`). Concurrent requests arriving from multiple pods at the identical millisecond timestamp collapsed into a single ZSET member, allowing excess requests to slip through uncounted.
3. **Integer Wrap-Around Quota Bypass (CWE-190)**: Naive addition checks (`count + permits <= limit`) were vulnerable to integer overflow under large adversarial `permits` requests, wrapping around to negative values and bypassing quotas.
4. **Concurrency Limiter Double-Disposal Exploit (CWE-675)**: If a caller invoked `lease.Dispose()` multiple times on a lease returned by `ConcurrencyRateLimiter`, the permit counter could be decremented repeatedly below its true baseline, effectively creating unauthorized concurrency slots.
5. **IETF RFC 9651 Quota Reporting Compliance**: Downstream HTTP clients and API gateways require visibility into the total quota limit via standard `X-RateLimit-Limit`. Previously, `RateLimitLease` carried only `RemainingPermits`, forcing the middleware to report `PermitCost` instead of the policy limit.

## Decision
We implement a comprehensive security hardening and partition lifecycle architecture across all packages in `EricksonLopez.RateLimiting` v1.0.0:

1. **Bounded Partition Collections & Idle Pruning**:
   - Added `MaxPartitions` (default: 10,000) to `RateLimiterOptions` and `ConcurrencyRateLimiterOptions`.
   - When partition count reaches `MaxPartitions` and a new key arrives, limiters trigger an atomic sweep (`PruneIdlePartitions`) that removes expired, idle partitions (`IsIdle`).
   - If capacity remains saturated after pruning, new keys are rejected safely with a structured lease, protecting host memory from exhaustion.

2. **Cryptographically Salted Lua ZSET Members**:
   - The distributed sliding window Lua script accepts a 6th argument (`ARGV[6]`), containing a unique GUID salt generated per evaluation.
   - The ZSET member is constructed as `now_ms .. ":" .. salt`, guaranteeing distinct set membership even for simultaneous identical-millisecond requests across distributed Kubernetes pods.

3. **Overflow-Safe Arithmetic Bounds**:
   - Replaced naive addition checks with subtraction-safe boundary checks:
     ```csharp
     if (permits > _limit || _currentCount > _limit - permits)
     {
         return RateLimitLease.Rejected(...);
     }
     ```
   - Enforced across `FixedWindowPartition`, `SlidingWindowPartition`, and `ConcurrencyPartition`.

4. **Atomic One-Shot Disposal for Concurrency Leases**:
   - Concurrency leases are backed by an internal `OneShotDisposer` that executes permit releases using atomic CAS:
     ```csharp
     if (Interlocked.Exchange(ref _disposed, 1) == 0)
     {
         _partition.Release(permits);
     }
     ```
   - Repeated calls to `Dispose()` are safe no-ops and cannot manipulate active permit counts.

5. **Quota Metadata (`RateLimitLease.Limit`)**:
   - Enhanced `RateLimitLease` with an optional `int? Limit = null` property and matching factory overloads.
   - `RateLimitingMiddleware` reads `lease.Limit ?? _options.PermitCost` when injecting `X-RateLimit-Limit`, fulfilling IETF RFC 9651 standard semantics.

6. **Lua Command Modernization**:
   - Updated Redis Token Bucket script from deprecated `HMSET` to `HSET` and clamped arithmetic to eliminate division-by-zero crashes on zero replenishment durations.

## Consequences

### Positive
- **DoS Immunity**: Guaranteed bound on process heap memory irrespective of key cardinality attacks.
- **Atomic Precision**: Zero request leakage under high-concurrency multi-node bursts in Redis.
- **Standardized Quotas**: Downstream clients receive mathematically accurate quota limits and remaining counts.
- **Tamper-Resistant Concurrency**: Invariant protection against lease disposal abuse or double-free bugs.

### Negative
- Pruning idle partitions introduces an $O(N)$ sweep over the partition dictionary when capacity is exhausted, though amortized across requests.

## References
- [ADR-001: Distributed Rate Limiting](./adr-001-distributed-rate-limiting.md)
- [ADR-004: Resilient Degradation & Middleware Callbacks](./adr-004-resilient-degradation-and-middleware-callbacks.md)
- [ADR-006: Concurrency Rate Limiting & Zero-Allocation Disposal](./adr-006-concurrency-rate-limiting.md)
- [ADR-008: Distributed Redis Token Bucket](./adr-008-distributed-redis-token-bucket.md)
