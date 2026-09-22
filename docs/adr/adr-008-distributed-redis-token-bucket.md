# ADR-008: Distributed Redis Token Bucket Rate Limiting Architecture

## Status
Accepted

## Date
2026-09-04

## Context
While sliding window algorithms are ideal for discrete request quotas, the **Token Bucket** algorithm provides a superior mechanism for traffic shaping and handling bursty workloads with continuous replenishment. In a token bucket system, tokens accumulate at a constant fill rate up to a fixed bucket capacity, allowing callers to burst while strictly bounding long-term average throughput.

In distributed microservices operating behind load balancers, token bucket state must be shared across all nodes. Naive implementations using Redis `INCR` or multi-command transactions suffer from race conditions, clock drift between application nodes, and network latency overhead.

We required a distributed token bucket limiter that:
1. Executes atomically in a single Redis network round-trip.
2. Supports continuous, high-precision token replenishment calculated at microsecond resolution.
3. Automatically computes exact mathematical `Retry-After` periods when tokens are exhausted.
4. Preserves 100% Native AOT compatibility and zero managed heap allocations.

## Decision
We implement `RedisTokenBucketRateLimiter` in `EricksonLopez.RateLimiting.Redis`:

1. **Redis Hash Data Representation**:
   - Each rate-limited partition key stores a Redis Hash (`HSET`) containing two fields:
     - `tokens`: Current floating-point count of available tokens.
     - `last_updated`: Unix timestamp of the last token calculation in microseconds.

2. **Atomic Single Round-Trip Lua Script**:
   ```lua
   local key = KEYS[1]
   local now = tonumber(ARGV[1])
   local max_tokens = tonumber(ARGV[2])
   local tokens_per_period = tonumber(ARGV[3])
   local period_us = tonumber(ARGV[4])
   local requested = tonumber(ARGV[5])

   local data = redis.call('HMGET', key, 'tokens', 'last_updated')
   local tokens = tonumber(data[1])
   local last_updated = tonumber(data[2])

   if not tokens or not last_updated then
       tokens = max_tokens
       last_updated = now
   else
       local elapsed = now - last_updated
       if elapsed > 0 then
           local fill_rate = tokens_per_period / period_us
           local generated = elapsed * fill_rate
           tokens = math.min(max_tokens, tokens + generated)
           last_updated = now
       end
   end
   ...
   ```
   - Operates in a single evaluation round-trip, eliminating race conditions between horizontal instances.
   - Computes fractional token generation precisely, avoiding discrete step artifacts.

3. **Microsecond-Accurate Retry-After**:
   - When requested permits exceed available tokens, the script calculates:
     $$\text{retry\_after\_us} = \left\lceil \frac{\text{requested} - \text{tokens}}{\text{fill\_rate}} \right\rceil$$
   - Eliminates premature retry thrashing from API consumers.

4. **Self-Pruning TTL**:
   - Every evaluation refreshes the Redis key TTL via `PEXPIRE key ttl_ms`. The TTL is dynamically computed to allow a completely drained bucket to refill plus a safety margin, ensuring that inactive keys do not consume Redis memory indefinitely.

5. **Railway-Oriented Degradation**:
   - All Redis operations are wrapped in try/catch mapping `RedisException` to `Result<RateLimitLease>.Failure(RateLimitErrors.ConnectionFailed(ex.Message))` for clean Fail-Open or Fail-Closed handling.

## Consequences

### Positive
- **Distributed Smooth Throttling**: Accommodates legitimate request bursts without penalizing sustained throughput.
- **Microsecond Precision**: Smooth token replenishment regardless of irregular request intervals.
- **Memory Efficient**: Redis Hash stores only two numeric fields per partition, compared to sorted sets which scale with the number of recent requests.
- **Native AOT Verified**: Lua script compiled from string literal; zero reflection.

### Negative
- Requires Redis infrastructure running in the deployment environment.

## References
- [ADR-001: Distributed Rate Limiting](./adr-001-distributed-rate-limiting.md)
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
- [ADR-004: Resilient Degradation & Middleware Callbacks](./adr-004-resilient-degradation-and-middleware-callbacks.md)
