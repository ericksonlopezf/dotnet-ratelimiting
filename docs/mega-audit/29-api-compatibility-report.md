# API COMPATIBILITY, TARGET FRAMEWORKS & EVOLUTION REPORT

**Document ID:** AUD-29-COMPAT  
**Date:** 2026-09-05  
**Audited Target:** Binary & Source Compatibility Matrix  

---

## 1. Target Framework Compatibility Matrix

All assemblies in the `EricksonLopez.RateLimiting` ecosystem target the modern supported .NET LTS and STS platforms via multi-targeting:
$$\text{TargetFrameworks: } \texttt{net8.0;net9.0;net10.0}$$

| Assembly | .NET 8.0 (LTS) | .NET 9.0 (STS) | .NET 10.0 (Current) | Native AOT Supported | Notes |
|---|---|---|---|---|---|
| `EricksonLopez.RateLimiting` | ✅ Compatible | ✅ Compatible | ✅ Compatible | ✅ Yes | Core Tier 0 Engine |
| `EricksonLopez.RateLimiting.AspNetCore` | ✅ Compatible | ✅ Compatible | ✅ Compatible | ✅ Yes | Minimal API & Middleware |
| `EricksonLopez.RateLimiting.Redis` | ✅ Compatible | ✅ Compatible | ✅ Compatible | ✅ Yes | StackExchange.Redis Adapter |

---

## 2. Binary & Source Backward Compatibility Analysis

### 2.1 Struct Layout & Evolution (`RateLimitLease`)
- **v1.0.0 Interface:** `RateLimitLease(bool IsAcquired, int RemainingPermits, TimeSpan? RetryAfter, DateTimeOffset? ResetTime)`
- **v1.1.0 Evolution:** Added `Action? DisposeAction = null` (for Concurrency Limiter release).
- **v1.2.0 Evolution:** Added `int? Limit = null` (for X-RateLimit-Limit quota reporting).
- **Compatibility Mechanism:**
  - Preserved existing 4-parameter and 5-parameter constructors.
  - Preserved 4-component and 5-component `Deconstruct` methods.
  - **Verdict:** **100% Binary & Source Backward Compatible.** Existing compiled consumer assemblies continue to link and execute without missing method exceptions (`MissingMethodException`).

### 2.2 Endpoint Convention Extensions Evolution
- **v1.0.0 Aliases:** `RequireDistributedRateLimiting` and `DisableDistributedRateLimiting`.
- **v1.2.0 Unified API:** `RequireRateLimiting` and `DisableRateLimiting`.
- **Compatibility Mechanism:**
  - Aliases forward directly to the unified extension methods.
  - **Verdict:** Zero breakage for legacy v1.0.0 consumers.

---

## 3. Forward Compatibility & v2.0 Breaking Changes Policy

The following planned architectural enhancements are scheduled for the **v2.0.0 Major Milestone** to adhere strictly to Semantic Versioning:
1. **`ValueTask` Migration (Breaking Signature Change):**
   ```csharp
   ValueTask<Result<RateLimitLease>> AcquireAsync(...);
   ```
   Will break binary compatibility for callers awaiting `Task`. Requires major version bump.
2. **Deprecation of `RequireDistributedRateLimiting` Aliases:**
   Will be marked `[Obsolete]` in v1.3.0 and removed in v2.0.0.
