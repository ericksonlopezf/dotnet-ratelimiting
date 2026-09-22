# ALGORITHM FORENSIC & MATHEMATICAL AUDIT

**Document ID:** AUD-03-ALGO  
**Date:** 2026-09-05  
**Audited Targets:** All 7 Rate Limiting Algorithms in `EricksonLopez.RateLimiting`  

---

## 1. Executive Summary & Algorithm Inventory

The framework implements 7 distinct rate limiting algorithms across in-memory and distributed tiers:
1. **In-Memory Fixed Window** (`FixedWindowPartition`)
2. **In-Memory Segmented Sliding Window** (`SlidingWindowPartition`)
3. **In-Memory Continuous Token Bucket** (`TokenBucketPartition`)
4. **In-Memory Concurrency Limiter** (`ConcurrencyPartition`)
5. **In-Memory Composite Pipeline** (`CompositeRateLimiter`)
6. **Distributed Redis Sliding Window** (`RedisSlidingWindowRateLimiter`)
7. **Distributed Redis Token Bucket** (`RedisTokenBucketRateLimiter`)

Every algorithm was evaluated against formal mathematical bounds, discrete time transitions, clock retrocession, arithmetic overflow, and boundary bypass vulnerabilities.

---

## 2. Deep Dive 1: In-Memory Fixed Window (`FixedWindowPartition`)

### 2.1 Formal Specification
- **ALGORITHM:** Discrete epoch window counting. Time is partitioned into discrete intervals of length $W$ ticks:
  $$\text{WindowIndex}(t) = \lfloor t / W \rfloor$$
- **INVARIANTS:**
  1. $0 \le \text{Count} \le \text{PermitLimit}$
  2. For all $t$ within window index $I$, $\sum \text{granted permits} \le \text{PermitLimit}$.
  3. Window transitions reset $\text{Count} \leftarrow 0$ atomically.
  4. Monotonicity: A backward clock jump ($t_1 < t_0$) must NEVER retrocede the window index.
- **STATE MODEL:**
  ```text
  _count: int (current accumulated permits leased in current window)
  _lastWindowIndex: long (index of the active window)
  _lock: object (synchronization monitor)
  ```
- **TRANSITIONS:**
  1. Arrival at time $t$: Calculate $\text{idx} = \max(\text{lastIdx}, t / W)$.
  2. If $\text{idx} > \text{lastIdx}$: Set $\text{count} \leftarrow 0$, $\text{lastIdx} \leftarrow \text{idx}$.
  3. If $\text{permits} \le \text{Limit} - \text{count}$: $\text{count} \leftarrow \text{count} + \text{permits} \implies$ **ACQUIRE**.
  4. Otherwise $\implies$ **REJECT**.
- **EDGE CASES & MATH BOUNDS:**
  - **Clock Backwards Jump:** Handled via `Math.Max(_lastWindowIndex, now.UtcTicks / _window.Ticks)`. If clock retrocedes, `_lastWindowIndex` does not move backward, preventing premature counter resets.
  - **Boundary Burst Bypass (Window 2x Traffic):** Inherent to Fixed Window. An attacker can send $L$ requests at $t = W - \epsilon$ and another $L$ requests at $t = W + \epsilon$, effectively executing $2L$ requests in a $2\epsilon$ interval. This is an expected fundamental characteristic of Fixed Window algorithms, mitigated by Sliding Window.
  - **Integer Overflow in Permits:** Checked in `TryAcquire`: `permits <= _permitLimit && _count <= _permitLimit - permits`. The subtraction `_permitLimit - permits` is guaranteed safe against signed 32-bit integer overflow.

---

## 3. Deep Dive 2: In-Memory Segmented Sliding Window (`SlidingWindowPartition`)

### 3.1 Formal Specification
- **ALGORITHM:** Segmented circular buffer approximation of continuous sliding window. The window $W$ is divided into $N$ equal segments of duration $S = W / N$.
- **INVARIANTS:**
  1. $\sum_{i=0}^{N-1} \text{Segments}[i] \le \text{PermitLimit}$
  2. Advancing $k$ segments clears exactly the intervening circular buffer slots.
  3. If advancing $k \ge N$, all $N$ slots are reset to 0.
  4. `RetryAfter` must equal the exact duration until sufficient permits roll out of the window.
- **STATE MODEL:**
  ```text
  _segments: int[] (length N, circular ring buffer)
  _segmentInterval: TimeSpan (S = W / N)
  _lastSegmentIndex: long (discrete index of latest observed segment)
  _lock: object (synchronization monitor)
  ```
- **TRANSITIONS:**
  1. Arrival at $t$: Compute `rawIdx = t / S.Ticks`.
  2. Monotonic index: `currentIdx = Math.Max(_lastSegmentIndex, rawIdx)`.
  3. `segmentsToAdvance = currentIdx - _lastSegmentIndex`.
  4. If `segmentsToAdvance > 0`:
     - `clearCount = min(segmentsToAdvance, N)`.
     - Clear slots `(_lastSegmentIndex + i) % N` for $i \in [1, \text{clearCount}]$.
     - Set `_lastSegmentIndex = currentIdx`.
  5. Sum current usage: $U = \sum_{i=0}^{N-1} \text{Segments}[i]$.
  6. If $\text{permits} \le \text{Limit} - U$: Add to active segment `Segments[currentIdx % N] += permits` $\implies$ **ACQUIRE**.
  7. Else: Loop over oldest segments to find exact rollout tick where freed permits $\ge \text{needed}$ $\implies$ **REJECT**.
- **EDGE CASES & MATH BOUNDS:**
  - **Modulo Operator in C#:** C# `%` operator can return negative values for negative integers. The implementation guards against this using:
    $$(((\text{segIdx} \pmod N) + N) \pmod N)$$
    guaranteeing non-negative array indices under all inputs.
  - **NTP Clock Skew Backwards:** Guaranteed safe by `Math.Max(_lastSegmentIndex, rawSegmentIndex)`.
  - **Segment Interval Zero Division:** Guarded in constructor: `intervalTicks = Math.Max(1, window.Ticks / _segments.Length)`.

---

## 4. Deep Dive 3: In-Memory Continuous Token Bucket (`TokenBucketPartition`)

### 4.1 Formal Specification
- **ALGORITHM:** Continuous token replenishment using floating-point timestamp delta.
  $$\text{Tokens}(t) = \min\left(\text{Capacity}, \text{Tokens}(t_{\text{last}}) + (t - t_{\text{last}}) \times \text{RefillRate}\right)$$
- **INVARIANTS:**
  1. $0.0 \le \text{CurrentTokens} \le \text{Capacity}$
  2. $\text{RefillRate} = \text{Capacity} / \text{Window.TotalSeconds}$
  3. Fractional tokens accumulate with continuous precision without rounding bias.
  4. Idle partitions with $\text{CurrentTokens} = \text{Capacity}$ can be scavenged.
- **STATE MODEL:**
  ```text
  _capacity: double
  _refillRatePerSecond: double
  _currentTokens: double
  _lastRefillTime: DateTimeOffset
  _lock: object
  ```
- **TRANSITIONS:**
  1. Arrival at $t$: $\Delta t = (t - t_{\text{last}}).\text{TotalSeconds}$.
  2. If $\Delta t > 0$: $\text{currentTokens} \leftarrow \min(\text{capacity}, \text{currentTokens} + \Delta t \times \text{refillRate})$, $t_{\text{last}} \leftarrow t$.
  3. If $\text{currentTokens} \ge \text{permits}$: $\text{currentTokens} \leftarrow \text{currentTokens} - \text{permits} \implies$ **ACQUIRE**.
  4. Else: $\text{missing} = \min(\text{capacity}, \text{permits} - \text{currentTokens})$, $\text{retryAfter} = \text{missing} / \text{refillRate} \implies$ **REJECT**.
- **EDGE CASES & MATH BOUNDS:**
  - **Floating Point NaN / Infinity in Retry-After:** Guarded at lines 44-47:
    `if (double.IsInfinity(secondsToWait) || double.IsNaN(secondsToWait) || secondsToWait > 86400.0) { secondsToWait = 86400.0; }`
  - **Drained Bucket Idle Scavenging:** `IsIdle(now)` simulates token refill:
    `simulatedTokens = Math.Min(_capacity, _currentTokens + (elapsedSeconds * _refillRatePerSecond)); return simulatedTokens >= _capacity;`
    Ensures that empty buckets that haven't received requests are correctly recognized as full and eligible for eviction.

---

## 5. Deep Dive 4: In-Memory Lock-Free Concurrency Limiter (`ConcurrencyPartition`)

### 5.1 Formal Specification
- **ALGORITHM:** Atomic Compare-And-Swap (CAS) state machine for parallel execution throttling.
- **INVARIANTS:**
  1. $-1 \le \text{ActivePermits} \le \text{Limit}$
  2. State $-1$ represents `RetiredState` (partition marked dead for GC eviction).
  3. Once transitioned to $-1$, no acquisition can ever succeed on that partition instance.
  4. Releases on retired partitions are safe no-ops.
  5. Lease disposal is one-shot idempotent (`Interlocked.Exchange(ref _disposed, 1) == 0`).
- **STATE MODEL:**
  ```text
  _activePermits: int (volatile, Interlocked CAS target)
  _limit: int (immutable maximum concurrency capacity)
  ```
- **STATE MACHINE TRANSITIONS:**
  ```text
  [0 Active Permits] ────(TryRetire CAS: 0 -> -1)────► [Retired (-1)] (Eligible for Eviction)
         │                                                      ▲
         │ (Acquire CAS: c -> c + p)                            │
         ▼                                                      │
  [1..Limit Active] ────(Release CAS: c -> c - p)───────────────┘
  ```
- **EDGE CASES & MATH BOUNDS:**
  - **ABA Race in Partition Eviction:** If Thread A tries to acquire while Thread B evicts:
    - Thread B sets `_activePermits` to $-1$.
    - Thread A's `CompareExchange` expects 0, but fails because state is $-1$.
    - Thread A re-reads state, detects `RetiredState`, removes entry from `_partitions`, and retries with a fresh partition. Zero lost permits, zero race conditions.
  - **Double Release / Dispose:** `OneShotDisposer` uses `Interlocked.Exchange(ref _disposed, 1)`. Calling `Dispose()` 1,000 times releases permits exactly once.

---

## 6. Deep Dive 5: Distributed Redis Sliding Window (`RedisSlidingWindowRateLimiter`)

### 6.1 Formal Specification
- **ALGORITHM:** Distributed sliding window using Redis Sorted Sets (ZSET).
  - Key: `RedisKey` (e.g. `rl:user123`)
  - Score: Unix timestamp in microseconds ($t_{\mu s}$)
  - Member: `$t_{\mu s}:requestId:index$` (Guarantees member uniqueness under microsecond concurrency)
- **ATOMIC LUA SCRIPT AUDIT:**
  ```lua
  local key = KEYS[1]
  local window_start = tonumber(ARGV[1])
  local now = tonumber(ARGV[2])
  local max_permits = tonumber(ARGV[3])
  local window_us = tonumber(ARGV[4])
  local requested = tonumber(ARGV[5])
  local request_id = ARGV[6]

  redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
  local current = redis.call('ZCARD', key)
  local remaining = max_permits - current

  if remaining >= requested then
      for i = 1, requested do
          redis.call('ZADD', key, now, now .. ':' .. request_id .. ':' .. i)
      end
      redis.call('PEXPIRE', key, math.ceil(window_us / 1000))
      remaining = remaining - requested
      return { 1, remaining, 0, now + window_us }
  else
      -- Rejection retry-after computation
      local needed = requested - remaining
      local oldest = redis.call('ZRANGE', key, 0, -1, 'WITHSCORES')
      local retry_after = 0
      if #oldest >= 2 then
          local target_idx = math.min(needed, math.floor(#oldest / 2)) * 2
          if target_idx >= 2 and target_idx <= #oldest then
              retry_after = tonumber(oldest[target_idx]) + window_us - now
              if retry_after < 0 then retry_after = 0 end
          end
      end
      if requested > max_permits then retry_after = window_us end
      return { 0, 0, retry_after, now + window_us }
  end
  ```
- **FINDINGS & FORENSIC ANALYSIS:**
  1. **[LOW] ALGO-REDIS-01: ZRANGE 0 -1 WITHSCORES Memory Scale:** On rejection, the Lua script executes `ZRANGE key 0 -1 WITHSCORES` which pulls the entire ZSET into Lua memory. For high `max_permits` (e.g. 50,000 requests/min), loading 100,000 elements on rejection creates unnecessary memory pressure in Redis.
     - *Remediation:* Limit `ZRANGE` to only the elements needed to compute `target_idx`: `ZRANGE key 0 target_offset WITHSCORES`.
  2. **Timestamp Resolution Alignment:**
     - In C#: `nowUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;`.
     - Timestamp has millisecond granularity scaled to microseconds. The unique `requestId:index` suffix ensures members never collide even when 1,000 permits are acquired in the same millisecond.

---

## 7. Deep Dive 6: Distributed Redis Token Bucket (`RedisTokenBucketRateLimiter`)

### 7.1 Formal Specification
- **ALGORITHM:** Distributed token bucket using Redis Hash (`HMGET` / `HSET`).
  - Fields: `tokens` (string float), `last_updated` (microsecond timestamp).
- **ATOMIC LUA SCRIPT AUDIT:**
  ```lua
  local data = redis.call('HMGET', key, 'tokens', 'last_updated')
  ...
  local elapsed = now - last_updated
  if elapsed > 0 then
      local generated = elapsed * fill_rate
      tokens = math.min(max_tokens, tokens + generated)
      last_updated = now
  end
  ```
- **SAFETY INVARIANTS VERIFIED:**
  - **Zero Division Protection:** Lines 39-40 and 47 explicitly prevent division by zero:
    `if period_us <= 0 then period_us = 1000000 end`
    `if tokens_per_period <= 0 then tokens_per_period = 1 end`
    `if fill_rate <= 0 then fill_rate = 0.000001 end`
  - **State Persistence on Rejection:** When `tokens < requested`, line 81 updates the refilled tokens and `last_updated` back into the Redis Hash so accrued tokens are not lost.
  - **TTL Scavenging:** `ttl_ms` is set to $2\times$ the full refill duration (minimum 60 seconds), ensuring inactive keys are automatically purged by Redis eviction.

---

## 8. Mathematical Boundary Matrix

| Edge Case Condition | Fixed Window | Sliding Window | Token Bucket | Concurrency | Redis Sliding | Redis Token Bucket |
|---|---|---|---|---|---|---|
| `permits = 0` | Rejected (`ArgumentOutOfRangeException`) | Rejected | Rejected | Rejected | Rejected | Rejected |
| `permits < 0` | Rejected (`ArgumentOutOfRangeException`) | Rejected | Rejected | Rejected | Rejected | Rejected |
| `permits > Limit` | Rejected with `RetryAfter = Window` | Rejected with `RetryAfter = Window` | Rejected with `RetryAfter = FullRefill` | Rejected with `Rejected` | Rejected with `RetryAfter = Window` | Rejected with `RetryAfter = FullRefill` |
| Clock Skew Backwards | Protected (`Math.Max`) | Protected (`Math.Max`) | Protected ($\Delta t \le 0$ no-op) | Immune (No clock used) | Network-dependent | Network-dependent |
| Burst to 100% capacity | Permitted | Permitted | Permitted | Permitted | Permitted | Permitted |
| High Concurrency (1024T)| Atomic (Lock) | Atomic (Lock) | Atomic (Lock) | Lock-Free (CAS) | Redis Atomic Lua | Redis Atomic Lua |
