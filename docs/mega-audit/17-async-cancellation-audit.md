# ASYNC, TASK LIFECYCLE & CANCELLATION TOKEN AUDIT

**Document ID:** AUD-17-CANCEL  
**Date:** 2026-09-05  
**Audited Target:** Asynchronous Pipelines & `CancellationToken` Observation  

---

## 1. Executive Summary

Asynchronous programming in infrastructure libraries requires strict cooperative cancellation and resource protection.
Failure to honor `CancellationToken` leads to:
1. **Permit Leaks:** Concurrency slots acquired before cancellation that are never released.
2. **Orphaned Tasks:** Background operations running after an HTTP connection is aborted.
3. **Inconsistent State:** Partial state transitions in composite chains.

The audit verified cancellation handling across pre-invocation, in-flight execution, and rollback cleanup.

---

## 2. Cancellation Invariants & Verification Matrix

| Invariant | Component | Code Implementation | Status |
|---|---|---|---|
| **Pre-invocation check** | All Rate Limiters | `cancellationToken.ThrowIfCancellationRequested();` at method entry. | ✅ 100% Enforced |
| **In-flight Redis cancellation** | `RedisSlidingWindow`, `RedisTokenBucket` | `.WaitAsync(cancellationToken)` on Redis async script evaluation. | ✅ 100% Enforced |
| **Composite chain cancellation** | `CompositeRateLimiter` | `try { ... } catch { Rollback(acquiredLeases); throw; }` | ✅ 100% Enforced |
| **HTTP middleware cancellation** | `RateLimitingMiddleware` | Passes `context.RequestAborted` to `AcquireAsync` and callbacks. | ✅ 100% Enforced |

---

## 3. Deep Dive: Composite Cancellation & Concurrency Permit Leakage

### 3.1 The Failure Mode Investigated
In `CompositeRateLimiter`, child limiters are evaluated in sequential order:
- Suppose Limiter 1 is a `ConcurrencyRateLimiter` with `Limit = 1`.
- Limiter 2 is an external slow or remote limiter.
- Step 1: Limiter 1 acquires 1 slot (`activePermits = 1`).
- Step 2: The HTTP client disconnects (`context.RequestAborted.IsCancellationRequested == true`).
- If `CompositeRateLimiter` does not catch `OperationCanceledException` and dispose the lease from Limiter 1, `activePermits` remains 1 forever!
- All future requests for that partition are permanently blocked!

### 3.2 Mitigation Verification in Code
In `CompositeRateLimiter.cs`:
```csharp
var acquiredLeases = new List<RateLimitLease>(_limiters.Length);
try
{
    for (int i = 0; i < _limiters.Length; i++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limiter = _limiters[i];
        var result = await limiter.AcquireAsync(key, permits, cancellationToken).ConfigureAwait(false);
        ...
        acquiredLeases.Add(lease);
    }
}
catch
{
    Rollback(acquiredLeases);
    throw;
}
```
And `Rollback`:
```csharp
private static void Rollback(List<RateLimitLease> leases)
{
    for (int j = 0; j < leases.Count; j++)
    {
        leases[j].Dispose();
    }
    leases.Clear();
}
```
- Verified by automated regression test `MegaAuditAdversarialSuite.CompositeLimiter_CancellationDuringChain_MustNotLeakConcurrencyPermits`.
- **Verdict:** **CONCURRENCY SLOTS ARE FULLY RECLAIMED UPON CANCELLATION.**

---

## 4. Cancellation in Redis Network Calls

In `RedisSlidingWindowRateLimiter.cs`:
```csharp
var result = await db.ScriptEvaluateAsync(
    SlidingWindowLua,
    keys: [(RedisKey)fullKey],
    values: [...]
).WaitAsync(cancellationToken).ConfigureAwait(false);
```
- When `cancellationToken` cancels while waiting for Redis, `.WaitAsync(cancellationToken)` terminates the awaiter immediately, throwing `OperationCanceledException` to the caller.
- Verified by tests `MegaAuditRedisAdversarialSuite.RedisSlidingWindow_MidFlightCancellation_MustThrowOperationCanceledException` and `RedisTokenBucket_MidFlightCancellation_MustThrowOperationCanceledException`.
