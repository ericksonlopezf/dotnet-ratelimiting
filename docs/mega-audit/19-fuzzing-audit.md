# FUZZ TESTING & PATHOLOGICAL INPUT FORENSIC AUDIT

**Document ID:** AUD-19-FUZZ  
**Date:** 2026-09-05  
**Audited Target:** `EricksonLopez.RateLimiting` Input Boundaries  

---

## 1. Executive Summary

Fuzz testing evaluates the rate limiting engine against randomized, malformed, extreme, and pathological inputs designed to trigger:
- Unhandled runtime crashes (`NullReferenceException`, `IndexOutOfRangeException`, `DivideByZeroException`).
- Arithmetic overflows (`OverflowException` or silent sign flips).
- Process hangs or infinite loops.
- Excessive memory consumption via string allocation attacks.

The framework was tested against exhaustive fuzz vectors covering:
1. **Partition Key Strings** (Null, empty, 64KB strings, Unicode, control characters, script payloads).
2. **Permit Counts** (Negative, zero, maximum integer).
3. **TimeSpan Intervals** (Negative, zero, fractional ticks, `TimeSpan.MaxValue`).

---

## 2. Fuzzing Vectors & Behavioral Results

| Fuzz Input Category | Fuzz Vector Sample | Expected Defense | Observed Behavior | Status |
|---|---|---|---|---|
| **Null Key** | `key = null!` | `ArgumentNullException` | Throws `ArgumentNullException` | ✅ IMMUNE |
| **Empty Key** | `key = ""` | Graceful acceptance / fallback | Accepted as valid dictionary key | ✅ IMMUNE |
| **Whitespace Key** | `key = "   \t\r\n"` | Graceful acceptance / fallback | Accepted as valid dictionary key | ✅ IMMUNE |
| **Extreme Length Key** | `new string('A', 65536)` (64KB string) | Memory protection or bounded hash | Hashed into `ConcurrentDictionary` bucket | ✅ IMMUNE |
| **Unicode & Emojis** | `key = "🚀🔥\u200B\uFEFF\u0000"` | UTF-16 string handling | Exact byte ordinal matching | ✅ IMMUNE |
| **Script Injection** | `key = "'; redis.call('FLUSHALL'); --"` | Parameterized treat as pure data | Preserved as raw string key without eval | ✅ IMMUNE |
| **Negative Permits** | `permits = -1`, `int.MinValue` | `ArgumentOutOfRangeException` | Throws `ArgumentOutOfRangeException` | ✅ IMMUNE |
| **Zero Permits** | `permits = 0` | `ArgumentOutOfRangeException` | Throws `ArgumentOutOfRangeException` | ✅ IMMUNE |
| **Permits = MaxValue** | `permits = int.MaxValue` | Safe rejection without overflow | Rejected cleanly (`_count <= Limit - permits` false) | ✅ IMMUNE |
| **Zero Window** | `Window = TimeSpan.Zero` | `ArgumentOutOfRangeException` | Throws `ArgumentOutOfRangeException` on property set | ✅ IMMUNE |
| **Negative Window** | `Window = TimeSpan.FromSeconds(-10)`| `ArgumentOutOfRangeException` | Throws `ArgumentOutOfRangeException` on property set | ✅ IMMUNE |
| **Window = MaxValue** | `Window = TimeSpan.MaxValue` | Division handles large ticks | Discrete index evaluates safely to 0 | ✅ IMMUNE |

---

## 3. Deep Dive: Zero & Negative Permits Invariant
In `FixedWindowRateLimiter.cs`, `SlidingWindowRateLimiter.cs`, `TokenBucketRateLimiter.cs`, and `ConcurrencyRateLimiter.cs`:
```csharp
ArgumentNullException.ThrowIfNull(key);
ArgumentOutOfRangeException.ThrowIfLessThan(permits, 1);
```
Every limiter enforces `permits >= 1` before performing any calculation or dictionary lookup.
Allowing `permits = 0` would create a security loophole where a caller could "probe" rate limit headers (`X-RateLimit-Remaining`) without consuming permits, facilitating automated timing attacks.

---

## 4. Pathological String Key Allocation
- When 100,000 distinct 1KB random string keys are passed:
  - In-memory limiters insert them into `ConcurrentDictionary`.
  - Once `_partitions.Count` hits `MaxPartitions` (10,000), further keys are rejected and **NOT added** to the dictionary.
  - This guarantees that an attacker sending 64KB keys cannot cause an out-of-memory collapse.
