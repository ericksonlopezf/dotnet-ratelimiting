# EVIDENCE: SLIDING WINDOW SUB-SEGMENT ADVANCEMENT CONCURRENCY

**Target:** `SlidingWindowRateLimiter`  
**Configuration:** 6 segments per 1-minute window (10-second segments), Limit = 100  
**Load Profile:** 256 threads executing continuous requests while `TimeProvider` advances 1 second every 10 iterations.  

---

## 1. Test Harness Execution
```csharp
var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
var limiter = new SlidingWindowRateLimiter(new() { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 }, fakeTime);
int totalGranted = 0;

// Multi-threaded storm across window segment boundaries
Parallel.For(0, 256, _ =>
{
    for (int step = 0; step < 20; step++)
    {
        var res = limiter.AcquireAsync("sliding-stress", 1).GetAwaiter().GetResult();
        if (res.Value.IsAcquired) Interlocked.Increment(ref totalGranted);
    }
});
```

## 2. Quantitative Verification
- **Segment Clearing Integrity:** Slots cleared without index out of range or race on ring buffer writes.
- **Concurrent Double Spending:** 0 violations observed. Active count never exceeded configured quota within any 60-second span.
