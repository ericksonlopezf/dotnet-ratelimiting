# API DESIGN & PUBLIC CONTRACT FORENSIC AUDIT

**Document ID:** AUD-12-APIDESIGN  
**Date:** 2026-09-05  
**Audited Target:** Public API Surface of `EricksonLopez.RateLimiting.*`  

---

## 1. Executive Summary

The public API was evaluated from the perspective of an external framework consumer who has never seen the internal implementation.
A great framework API should **make the right thing easy and the wrong thing difficult** ("Pit of Success").

**Key Strengths:**
- High discoverability via `services.AddRateLimiting(builder => ...)` and `app.UseHttpRateLimiting()`.
- Fluent builder ergonomics in `RateLimiterPolicyBuilder` allow rapid configuration of multi-algorithm policies without constructor juggling.
- Zero mutable global state; all options are encapsulated.
- Strict parameter validation guards across constructors, properties, and methods.

**Areas for Improvement:**
- `RequireDistributedRateLimiting` and `DisableDistributedRateLimiting` are legacy aliases for `RequireRateLimiting` and `DisableRateLimiting`.
- The synchronous fast path returns `Task<Result<RateLimitLease>>` instead of `ValueTask<Result<RateLimitLease>>`.

---

## 2. API Design Dimension Scorecard

| Dimension | Score (/10) | Evaluation & Analysis |
|---|---|---|
| **Discoverability** | 9.5 / 10 | Top-level DI methods (`AddSlidingWindowRateLimiter`, `AddRateLimiting`, `UseHttpRateLimiting`) surface naturally in IntelliSense. |
| **Consistency** | 9.0 / 10 | Consistent naming patterns (`Add*RateLimiter`, `Add*RateLimiting`, `Options`, `Metrics`). `IRateLimiter` contract adhered to across in-memory and Redis. |
| **Safety (Fail-Fast)** | 9.5 / 10 | Options validate immediately on property setter: `PermitLimit < 1` throws `ArgumentOutOfRangeException` at configuration time, not runtime. |
| **Simplicity** | 9.0 / 10 | Standard default configuration works out of the box with zero required parameters. |
| **Flexibility** | 9.5 / 10 | Supports standalone in-memory limiters, composite multi-window limiters, named policies, and distributed Redis providers. |
| **Documentation (XML)** | 10.0 / 10 | 100% XML doc comment coverage across all public types, methods, parameters, and return values. Compiler warning CS1591 enforced as error. |
| **Misuse Resistance** | 8.5 / 10 | Struct-based `RateLimitLease` prevents null-ref bugs. Partition saturation under high-cardinality attacker keys requires documentation guidance. |
| **Extensibility** | 9.0 / 10 | `IRateLimiter` and `RateLimiterPolicyBuilder.AddPolicy` allow external custom rate limiting engines to integrate seamlessly. |
| **TOTAL API SCORE** | **74.0 / 80** | **92.5% (EXEMPLARY)** |

---

## 3. Redundancy & Ergonomics Analysis

### 3.1 Legacy Aliases in `EndpointRateLimitingExtensions`
- In `EndpointRateLimitingExtensions.cs`:
  ```csharp
  public static TBuilder RequireDistributedRateLimiting<TBuilder>(this TBuilder builder, string policyName)
      => builder.RequireRateLimiting(policyName);

  public static TBuilder DisableDistributedRateLimiting<TBuilder>(this TBuilder builder)
      => builder.DisableRateLimiting();
  ```
- **Finding:** The terms `RequireDistributedRateLimiting` and `DisableDistributedRateLimiting` were introduced during early v1.0 iterations to distinguish Redis endpoints from in-memory endpoints.
- In v1.2.0, all rate limiters—in-memory and distributed—are registered as named policies under the unified `RequireRateLimiting(policyName)` convention.
- **Recommendation:** Retain these aliases for backward compatibility, but mark them as candidates for cleanup in v2.0 to avoid confusing new developers.

---

## 4. Method Overload & Constructor Audit

1. **`RateLimitLease` Constructors:**
   - 4-parameter constructor: `(isAcquired, remainingPermits, retryAfter, resetTime)` — preserved for v1.0.0 binary backward compatibility.
   - 5-parameter constructor: `(..., disposeAction)` — added for v1.1.0 concurrency support.
   - 6-parameter primary constructor: `(..., disposeAction, limit)` — full current capability.
   - Preserves complete binary backward compatibility while enabling new features.
2. **`CompositeRateLimiter` Constructors:**
   - `IEnumerable<IRateLimiter>` and `params IRateLimiter[]` overloads provided. Throws `ArgumentException` if list is empty. Clean and idiomatic.
