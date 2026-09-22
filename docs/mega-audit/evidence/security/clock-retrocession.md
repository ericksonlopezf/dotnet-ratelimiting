# EVIDENCE: CLOCK RETROCESSION & NTP DRIFT EXPERIMENT

**Test Case:** Clock Skew Backwards Jump (-1 Hour)  
**Target:** `FixedWindowPartition`, `SlidingWindowPartition`, `TokenBucketPartition`  
**Execution Environment:** .NET 10.0 (x64) with `FakeTimeProvider`  

---

## 1. Test Description
Simulates an NTP time correction jumping the system clock backwards by 1 hour immediately following permit consumption.

```csharp
var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
var fakeTime = new FakeTimeProvider(now);
var limiter = new SlidingWindowRateLimiter(new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }, fakeTime);

// 1. Consume 5 permits (exhaust quota)
for (int i = 0; i < 5; i++)
{
    var res = await limiter.AcquireAsync("user-clock-test", 1);
    Assert.True(res.Value.IsAcquired);
}

// 2. Jump clock backwards by 1 hour
fakeTime.SetUtcNow(now.AddHours(-1));

// 3. Attempt to acquire: Must be REJECTED! (Clock retrocession must not reset the window early)
var blocked = await limiter.AcquireAsync("user-clock-test", 1);
Assert.False(blocked.Value.IsAcquired);
```

## 2. Experimental Result
- **Result:** **PASSED.**
- **Observed Behavior:** `Math.Max(_lastSegmentIndex, rawSegmentIndex)` prevents the ring buffer index from rolling backward. Zero counter reset occurred.
