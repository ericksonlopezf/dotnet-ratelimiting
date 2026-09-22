# OFFENSIVE SECURITY & RED TEAM ADVERSARIAL AUDIT

**Document ID:** AUD-07-SEC  
**Date:** 2026-09-05  
**Audited Target:** `EricksonLopez.RateLimiting.*` Security Boundaries  

---

## 1. Threat Profile & Red Team Objectives

The Red Team audit simulated real-world adversaries attempting to:
1. **Bypass the Rate Limiter (Quota Circumvention):** Exceed configured rate limits without triggering HTTP 429 Too Many Requests.
2. **Denial of Service via Key Poisoning (Noisy Neighbor Attack):** Exploit partition allocation to exhaust server memory or cause legitimate users to be throttled.
3. **Identity Spoofing:** Manipulate HTTP request headers to evade per-client limits.
4. **Infrastructure Disruption:** Force backend timeouts or connection pool exhaustion in distributed adapters.

---

## 2. Attack Vectors & Verification Results

### 2.1 Attack Vector 1: Rate-Limit Bypass via Header Manipulation
- **Hypothesis:** An attacker behind a reverse proxy manipulates headers (e.g. `X-Forwarded-For`, `Client-IP`, `X-Real-IP`, `Forwarded`) to rotate their apparent identity.
- **Analysis:**
  - `RateLimitingMiddlewareOptions.PartitionKeyResolver` defaults to:
    ```csharp
    context => context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"
    ```
  - `context.Connection.RemoteIpAddress` is populated directly by the ASP.NET Core transport (Kestrel / IIS / Socket).
  - It does **NOT** read `X-Forwarded-For` by default.
  - **Verdict:** An attacker injecting spoofed `X-Forwarded-For: 8.8.8.8` cannot bypass the rate limiter because Kestrel's `RemoteIpAddress` ignores untrusted headers unless `ForwardedHeadersMiddleware` is explicitly configured in the host pipeline.
  - **Operational Warning (SEC-01):** If the application runs behind a reverse proxy (e.g. AWS ALB / Cloudflare / Nginx) WITHOUT `UseForwardedHeaders()`, all clients will share the proxy's IP address and be throttled together under a single global bucket.

### 2.2 Attack Vector 2: Bypass via Path / Casing / Encoding Variants
- **Hypothesis:** Bypassing named endpoint policies (`RequireRateLimiting("tier1")`) by altering path casing or percent-encoding (e.g. `/api/orders` vs `/API/ORDERS` vs `/api/%6frders`).
- **Analysis:**
  - Endpoint metadata resolution occurs AFTER ASP.NET Core's routing engine has normalized the route and matched the endpoint:
    ```csharp
    var endpoint = context.GetEndpoint();
    var enableMeta = endpoint.Metadata.GetMetadata<IEnableRateLimitingMetadata>();
    ```
  - Route matching is case-insensitive by default in ASP.NET Core. Once matched, the endpoint metadata is attached to `context.GetEndpoint()`.
  - In `RateLimiterPolicyRegistry`:
    ```csharp
    private readonly ConcurrentDictionary<string, IRateLimiter> _policies = new(StringComparer.OrdinalIgnoreCase);
    ```
    Policy names are registered and retrieved case-insensitively.
  - **Verdict:** **IMMUNE.** Path casing variations and policy name casing variations do not bypass the policy.

### 2.3 Attack Vector 3: Key Poisoning & Cardinality Explosion
- **Hypothesis:** An attacker sends 1,000,000 requests, each with a randomized pseudo-IP or user ID, to cause an out-of-memory crash (OOM) via unbounded dictionary growth in `ConcurrentDictionary<string, Partition>`.
- **Analysis & Testing:**
  - All in-memory limiters (`FixedWindowRateLimiter`, `SlidingWindowRateLimiter`, `TokenBucketRateLimiter`, `ConcurrencyRateLimiter`) enforce a strict partition bound:
    ```csharp
    if (_partitions.Count >= _options.MaxPartitions && !_partitions.ContainsKey(key))
    {
        PruneIdlePartitions(now);
        if (_partitions.Count >= _options.MaxPartitions && !_partitions.ContainsKey(key))
        {
            var rejectLease = RateLimitLease.Rejected(_options.Window, now.Add(_options.Window), _options.PermitLimit);
            return Task.FromResult(Result<RateLimitLease>.Success(rejectLease));
        }
    }
    ```
  - When `_partitions.Count` hits `MaxPartitions` (default 10,000), it prunes idle partitions.
  - If memory is still full, it **rejects the new key with 429** rather than allowing the dictionary to grow unboundedly.
  - **Verdict:** **IMMUNE TO MEMORY EXHAUSTION.** The memory ceiling is strictly bounded by `MaxPartitions`.
  - **Vulnerability Finding (SEC-02 - Noisy Neighbor DoS):** See Chapter 08 for detailed analysis of partition saturation DoS.

### 2.4 Attack Vector 4: Redis Key Traversal / Script Injection
- **Hypothesis:** An attacker crafts malicious characters in the partition key (`key = "user'; redis.call('FLUSHALL'); --"`) to achieve arbitrary Lua command execution.
- **Analysis:**
  - Keys and arguments are parameterized using StackExchange.Redis `RedisKey` and `RedisValue` arrays:
    ```csharp
    keys: [(RedisKey)fullKey],
    values: [ windowStartUs, nowUs, ... ]
    ```
  - In Redis, Lua scripts are evaluated via `EVALSHA` / `EVAL` with separate `KEYS` and `ARGV` arrays. They are treated as pure data arguments, never concatenated into script source code.
  - **Verdict:** **IMMUNE TO SCRIPT INJECTION.**

---

## 3. Security Scoring & Vulnerability Catalog

| Vulnerability ID | Title | Severity | CWE | CVSS v3.1 | Status |
|---|---|---|---|---|---|
| **SEC-01** | Proxy Blindness under default `RemoteIpAddress` | Medium | CWE-350 | 5.3 | Documented Best Practice |
| **SEC-02** | Noisy-Neighbor Partition Saturation DoS | High | CWE-400 | 7.5 | Analyzed in Report 08 |
| **SEC-03** | Permanent Permit Loss in Composite Chaining | Medium | CWE-770 | 4.8 | Analyzed in Report 02 |
| **SEC-04** | Lua ZSET Full Memory Load on Rejection | Low | CWE-400 | 3.1 | Analyzed in Report 06 |

---

## 4. Red Team Recommendations
1. **Reverse Proxy Configuration:** Enforce documentation that ASP.NET Core hosts deployed behind proxies must invoke `app.UseForwardedHeaders()` before `app.UseHttpRateLimiting()`.
2. **Partition Eviction Strategy:** Replace strict rejection on `MaxPartitions` saturation with an LRU (Least Recently Used) or clock sweep eviction policy so active legitimate users are never blocked by malicious key flooding.
