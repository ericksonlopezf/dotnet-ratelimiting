# COMPARATIVE BENCHMARK: BCL VS ERICKSONLOPEZ RATE LIMITING

**Subject:** `System.Threading.RateLimiting` (BCL .NET 10) vs `EricksonLopez.RateLimiting`  

---

## 1. Feature Comparison

| Capability | BCL (`System.Threading.RateLimiting`) | `EricksonLopez.RateLimiting` |
|---|---|---|
| **In-Memory Fixed Window** | ✅ Yes | ✅ Yes |
| **In-Memory Sliding Window** | ✅ Yes | ✅ Yes |
| **In-Memory Token Bucket** | ✅ Yes | ✅ Yes |
| **In-Memory Concurrency Limiter** | ✅ Yes | ✅ Yes (Lock-free atomic CAS) |
| **Composite Multi-Interval Limiting** | ❌ No built-in chained composite | ✅ Yes (`CompositeRateLimiter`) |
| **Named Policy Registry & Fluent Builder** | ❌ Complex PartitionedRateLimiter | ✅ Yes (`RateLimiterPolicyBuilder`) |
| **Distributed Redis Sliding Window** | ❌ None (Third-party required) | ✅ Built-in (Atomic Lua script) |
| **Distributed Redis Token Bucket** | ❌ None | ✅ Built-in (Atomic Lua script) |
| **ASP.NET Core Middleware & Headers** | ✅ In AspNetCore package | ✅ Built-in RFC RateLimit headers |
| **Deterministic Result Monad** | ❌ Returns lease only | ✅ `Result<RateLimitLease>` with Fail-Closed/Open |
| **Synchronous Allocation** | **0 B (AttemptAcquire)** | **72 B (Task.FromResult)** |
