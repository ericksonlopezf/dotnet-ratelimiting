# SECURITY THREAT MODEL & STRIDE / DREAD ANALYSIS

**Document ID:** AUD-30-THREAT  
**Date:** 2026-09-05  
**Audited Target:** `EricksonLopez.RateLimiting.*` Security Architecture  

---

## 1. Threat Modeling Methodology

This threat model uses the **STRIDE** methodology (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege) combined with **DREAD** quantitative risk scoring (Damage, Reproducibility, Exploitability, Affected Users, Discoverability).

```text
       ┌─────────────────────────────────────────────────────────┐
       │                   ATTACK SURFACE MAP                    │
       ├─────────────────────────────────────────────────────────┤
       │  [Public Internet]                                      │
       │         │                                               │
       │         ▼                                               │
       │  [HTTP Ingress] ──► Headers (X-Forwarded-For, Tenant)   │
       │         │                                               │
       │         ▼                                               │
       │  [RateLimitingMiddleware]                               │
       │         │                                               │
       │         ├──► In-Memory Partitions (ConcurrentDictionary)│
       │         │                                               │
       │         └──► Redis (Socket / Lua Script Execution)      │
       └─────────────────────────────────────────────────────────┘
```

---

## 2. STRIDE Threat Analysis Matrix

| STRIDE Category | Threat Description | Attack Vector | Mitigation Status | Residual Risk |
|---|---|---|---|---|
| **Spoofing (S)** | Client spoofs IP address to bypass per-IP limits. | Attacker injects fake `X-Forwarded-For: 1.2.3.4`. | ASP.NET Core transport uses `RemoteIpAddress` by default. Requires host to configure `UseForwardedHeaders` securely. | **Low** (Operational) |
| **Tampering (T)** | Attacker injects Lua commands into partition key. | Malicious string in key: `'; redis.call(...);`. | Parameterized via StackExchange.Redis `RedisKey` and `RedisValue`. Treated as pure data. | **Zero** (Immune) |
| **Repudiation (R)**| Attacker claims request was throttled unfairly. | Disputes 429 response. | Rate limit metrics (`RequestsTotal`) and response headers (`X-RateLimit-*`) provide verifiable evidence. | **Zero** (Immune) |
| **Information Disclosure (I)**| Raw API keys or PII leaked in logs or metrics. | Unhashed API key passed as partition key. | OpenTelemetry metrics strictly omit keys. Redis logger logs keys at Debug/Warning; hashing recommended. | **Low** |
| **Denial of Service (D)**| Attacker floods 10,000 unique keys to exhaust partitions. | Randomized bot key flood saturating `MaxPartitions`. | In-memory limiter rejects new keys when capacity full (Finding DOS-01). Distributed Redis avoids this via LRU. | **Medium-High** |
| **Elevation of Privilege (E)**| Bypassing rate limit to brute-force auth endpoints. | Path casing/encoding tricks (`/login` vs `/LOGIN`). | Endpoint metadata matched after routing normalization; policy registry is case-insensitive. | **Zero** (Immune) |

---

## 3. DREAD Risk Scoring

Each threat is scored from 1 (Low) to 10 (Critical):
$$\text{DREAD Score} = \frac{\text{Damage} + \text{Reproducibility} + \text{Exploitability} + \text{Affected Users} + \text{Discoverability}}{5}$$

| Threat ID | Threat Title | D | R | E | A | D | Total DREAD (/10) | Severity |
|---|---|---|---|---|---|---|---|---|
| **T-01** | In-Memory Partition Saturation DoS (DOS-01) | 8 | 9 | 8 | 8 | 7 | **8.0 / 10** | **HIGH** |
| **T-02** | Proxy Blindness under default IP (SEC-01) | 6 | 8 | 7 | 8 | 6 | **7.0 / 10** | **MEDIUM** |
| **T-03** | Composite Limiter Token Burning (ARCH-02) | 5 | 8 | 6 | 4 | 5 | **5.6 / 10** | **MEDIUM** |
| **T-04** | Redis Lua Memory Spike on Rejection (REDIS-01) | 3 | 5 | 4 | 3 | 4 | **3.8 / 10** | **LOW** |

---

## 4. Defense-in-Depth Architecture Recommendations

1. **Secure Ingress Configuration:**
   Always pair `RateLimitingMiddleware` with `ForwardedHeadersOptions.KnownProxies` / `KnownNetworks` to prevent header spoofing from untrusted public networks.
2. **Key Normalization Pipeline:**
   Before passing user-provided identifiers into the rate limiter, apply:
   - Trim whitespace.
   - Lowercase normalization.
   - Cryptographic hashing (SHA-256) for tokens and passwords.
3. **Multi-Tiered Limiting:**
   Deploy a dual-layer strategy:
   - Layer 1: In-Memory Fixed/Sliding Window (10,000 req/min global IP DDoS shield).
   - Layer 2: Distributed Redis Token Bucket (Per-tenant/per-user quota).
