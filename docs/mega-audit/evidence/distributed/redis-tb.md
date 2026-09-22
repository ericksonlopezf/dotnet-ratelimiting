# EVIDENCE: DISTRIBUTED REDIS TOKEN BUCKET LUA SCRIPT VERIFICATION

**Target:** `RedisTokenBucketRateLimiter`  
**Storage:** Redis Hash (`HMGET` / `HSET`)  

---

## 1. Trace Execution
```lua
KEYS[1] = "rl:tb:client-corp"
ARGV[1] = 1757047200000000 -- now (us)
ARGV[2] = 100              -- max_tokens
ARGV[3] = 10               -- tokens_per_period
ARGV[4] = 1000000          -- period_us (1s)
ARGV[5] = 5                -- requested

-- Redis Server Execution
HMGET rl:tb:client-corp tokens last_updated -> nil, nil
-- Initializes with full capacity
tokens = 100 - 5 = 95
HSET rl:tb:client-corp tokens 95 last_updated 1757047200000000
PEXPIRE rl:tb:client-corp 60000
RETURN { 1, 95, 0, 1757047201000000 }
```

## 2. Inactivity Eviction
After 60 seconds without requests, Redis purges `rl:tb:client-corp` automatically via TTL expiration, preventing orphaned state in Redis.
