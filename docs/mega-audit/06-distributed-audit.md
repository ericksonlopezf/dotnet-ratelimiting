# DISTRIBUTED ARCHITECTURE & REDIS COORDINATION AUDIT

**Document ID:** AUD-06-DISTRIB  
**Date:** 2026-09-05  
**Audited Target:** `EricksonLopez.RateLimiting.Redis`  

---

## 1. Executive Summary

In a horizontally scaled environment (Kubernetes pods / container clusters), distributed rate limiting must ensure that:
$$\sum_{i=1}^{M} \text{Permits granted on Node } i \le \text{Global Quota}$$

Without atomic distributed coordination, concurrent requests distributed across Node A, Node B, Node C, and Node D will experience **severe double spending** if implementations rely on:
$$\text{Client READ} \implies \text{Client COMPUTE} \implies \text{Client WRITE}$$

`EricksonLopez.RateLimiting.Redis` **completely eliminates network-induced race conditions** by delegating state transitions to **single round-trip, atomic Lua scripts** executed on the Redis server engine.

---

## 2. Multi-Node Topology & Distributed State Model

```text
┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐
│  API Pod 1      │      │  API Pod 2      │      │  API Pod 3      │
│  (Node A)       │      │  (Node B)       │      │  (Node C)       │
└────────┬────────┘      └────────┬────────┘      └────────┬────────┘
         │                        │                        │
         │ Single Lua Eval        │ Single Lua Eval        │ Single Lua Eval
         │ (1 Network RTT)        │ (1 Network RTT)        │ (1 Network RTT)
         ▼                        ▼                        ▼
┌───────────────────────────────────────────────────────────────────┐
│                      REDIS CLUSTER / INSTANCE                     │
│  • Single-Threaded Lua Execution Engine (Atomicity Guaranteed)    │
│  • Redis ZSET: rl:tenant123 (Sliding Window microsecond scores)   │
│  • Redis Hash: rl:tb:tenant123 (Token Bucket tokens + timestamp)  │
└───────────────────────────────────────────────────────────────────┘
```

---

## 3. Lua Script Forensic Inspection

### 3.1 Distributed Sliding Window (`RedisSlidingWindowRateLimiter`)
- **Key:** `rl:{partition_key}`
- **Storage Primitive:** Sorted Set (ZSET)
- **Score:** Unix timestamp in microseconds ($t_{\mu s}$)
- **Member:** `$t_{\mu s}:requestId:index$`
- **Evaluation Steps (All Executed Atomically within Redis):**
  1. `ZREMRANGEBYSCORE key -inf window_start`: Prunes entries older than $(t - W)$.
  2. `ZCARD key`: Counts current active entries in the sliding window.
  3. `remaining = max_permits - current`.
  4. If `remaining >= requested`:
     - Loops $1..\text{requested}$, executing `ZADD key now now:requestId:i`.
     - `PEXPIRE key ceil(window_us / 1000)`: Sets/refreshes TTL to window duration.
     - Returns `{ 1, remaining - requested, 0, now + window_us }`.
  5. Else:
     - Calculates `needed = requested - remaining`.
     - Inspects oldest entries to compute precise `Retry-After`.
     - Returns `{ 0, 0, retry_after, now + window_us }`.

### 3.2 Distributed Token Bucket (`RedisTokenBucketRateLimiter`)
- **Key:** `rl:tb:{partition_key}`
- **Storage Primitive:** Hash (`tokens`, `last_updated`)
- **Evaluation Steps (Atomic):**
  1. `HMGET key tokens last_updated`: Fetches current state.
  2. If uninitialized: `tokens = max_tokens`, `last_updated = now`.
  3. Else: Calculates $\Delta t = \text{now} - \text{last\_updated}$. If $\Delta t > 0$, adds $\Delta t \times \text{fill\_rate}$, caps at $\text{max\_tokens}$.
  4. If `tokens >= requested`: Deducts `requested`, sets `allowed = 1`.
  5. Else: Computes wait time until tokens reach `requested`, sets `allowed = 0`.
  6. `HSET key tokens tokens last_updated last_updated`: Persists state.
  7. `PEXPIRE key ttl_ms`: Refreshes TTL ($2\times$ full refill duration, min 60s).

---

## 4. Distributed Invariants Audit Matrix

| Distributed Invariant | Assessment | Status | Evidence |
|---|---|---|---|
| **Atomicity Across Nodes** | Lua scripts execute atomically on the single-threaded Redis engine; no other command can interleave. | ✅ VERIFIED | Redis single-threaded execution model. |
| **Monotonic Allowance** | Sum of permits leased across all nodes never exceeds quota within any sliding window. | ✅ VERIFIED | ZSET count and Hash tokens are evaluated centrally. |
| **Zero Clock Drift Vulnerability** | Time $t$ is passed from the calling node: $t_{\text{node}}$. (See Finding DIST-01). | ⚠️ FINDING | Clock skew across nodes can cause slight discrepancy. |
| **Network Round Trips (RTT)** | Exactly 1 network round-trip per lease check. Zero multiple calls, zero pipelines. | ✅ VERIFIED | Single `db.ScriptEvaluateAsync`. |
| **Automatic State Eviction** | Inactive keys expire via Redis TTL (`PEXPIRE`), preventing unbounded Redis memory growth. | ✅ VERIFIED | Window TTL set on every acquire. |

---

## 5. Distributed Findings & Risk Assessment

### Finding DIST-01: Node-Side Timestamp in Distributed Coordination
- **Severity:** Low / Distributed Best Practice
- **Description:** In both `RedisSlidingWindowRateLimiter` and `RedisTokenBucketRateLimiter`, the timestamp (`nowUs`) is computed on the application node via `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L` and passed as `ARGV`.
- **Impact:** If Node A and Node B have significant NTP clock skew (e.g., Node B is 500ms behind Node A), Node B's requests will write older timestamps into the ZSET or Hash, slightly delaying or advancing token rollouts.
- **Remediation / Recommendation:** For extreme multi-node clock consistency, the Lua script can optionally fetch the Redis server time using `redis.call('TIME')` inside the Lua script itself, ensuring all nodes use the canonical Redis clock.

### Finding DIST-02: ZRANGE WITHSCORES Memory Footprint on Rejection
- **Severity:** Low / Performance
- **Description:** When a request is rejected in `RedisSlidingWindowRateLimiter`, the Lua script calls `local oldest = redis.call('ZRANGE', key, 0, -1, 'WITHSCORES')`, returning the entire sorted set into Lua memory.
- **Remediation:** Replace `ZRANGE key 0 -1 WITHSCORES` with `ZRANGE key 0 target_idx WITHSCORES` to fetch only the prefix of elements needed for the `Retry-After` calculation.
