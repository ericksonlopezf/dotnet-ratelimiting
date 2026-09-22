# REMEDIATION ROADMAP & ENGINEERING ACTION PLAN

**Document ID:** AUD-27-REMEDIATION  
**Date:** 2026-09-05  
**Audited Target:** `EricksonLopez.RateLimiting.*` Engineering Roadmap  

---

## 1. Remediation Phasing & Priority Matrix

| Phase | Milestone | Focus Areas | Findings Addressed | Target Delivery |
|---|---|---|---|---|
| **Phase 1: Immediate Guidance** | `v1.2.1` | Documentation updates, operational guidance callouts, mutation test gap closure. | `FINDING-SEC-01`, `FINDING-MUT-01`, `FINDING-ARCH-02` (guidance) | Immediate |
| **Phase 2: Redis Lua Optimization** | `v1.2.2` | Bounded ZRANGE retrieval on rejection in Redis sliding window Lua script. | `FINDING-REDIS-01` | Near-Term |
| **Phase 3: Resilient Eviction** | `v1.3.0` | `PartitionCache<TPartition>` with LRU/Clock-sweep eviction under capacity saturation. | `FINDING-DOS-01`, `FINDING-ARCH-01` | Mid-Term |
| **Phase 4: Zero-Allocation API** | `v2.0.0` | `ValueTask<Result<RateLimitLease>>` signature migration; cleanup of legacy aliases. | `FINDING-PERF-01`, `FINDING-API-01` | Next Major (Breaking) |

---

## 2. Detailed Technical Specifications for Remediations

### 2.1 Remediation for FINDING-DOS-01 (Partition Saturation DoS)
- **Target Component:** In-memory partition cache.
- **Implementation Strategy:**
  Introduce an internal `PartitionCache<TPartition>` holding:
  1. A `ConcurrentDictionary<string, CacheEntry>`.
  2. A lightweight lock-free or sampled LRU timestamp.
  3. When `Count >= MaxPartitions`:
     - First, prune expired/idle partitions (`IsIdle == true`).
     - If `Count` is STILL $\ge \text{MaxPartitions}$, discard the oldest accessed partition to make room for the new key, rather than rejecting the new request.
  4. This eliminates the noisy-neighbor denial of service while maintaining a hard ceiling on memory.

### 2.2 Remediation for FINDING-PERF-01 (`ValueTask` Migration)
- **Target Component:** `IRateLimiter.cs`.
- **Code Modification:**
  ```diff
  - Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default);
  + ValueTask<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default);
  ```
- **Performance Impact:**
  - Synchronous in-memory limiters return `new ValueTask<Result<RateLimitLease>>(Result<RateLimitLease>.Success(lease))` $\implies$ **0 BYTES ALLOCATED**.
  - Redis limiters await `ValueTask` directly without boxing.
  - Achieves the promised "Zero heap allocation per lease check" in compiled reality.

### 2.3 Remediation for FINDING-REDIS-01 (Bounded ZRANGE in Lua)
- **Target Component:** `RedisSlidingWindowRateLimiter.cs`.
- **Code Modification in Lua:**
  ```diff
  - local oldest = redis.call('ZRANGE', key, 0, -1, 'WITHSCORES')
  + local fetch_count = math.min(needed, 100) * 2 - 1
  + local oldest = redis.call('ZRANGE', key, 0, fetch_count, 'WITHSCORES')
  ```
- **Impact:** Eliminates transferring up to 100,000 array elements into Lua memory during high-quota rejections.

### 2.4 Remediation for FINDING-MUT-01 (Composite Fallback Test Gap)
- **Action:** Add unit test in `CompositeRateLimiterTests.cs`:
  ```csharp
  [Fact]
  public async Task AcquireAsync_ChildRejectsWithNullRetryAfter_DefaultsToOneSecond()
  {
      var mockLimiter = Substitute.For<IRateLimiter>();
      mockLimiter.AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
          .Returns(RateLimitLease.Rejected(retryAfter: TimeSpan.Zero)); // or null via constructor
      ...
  }
  ```
