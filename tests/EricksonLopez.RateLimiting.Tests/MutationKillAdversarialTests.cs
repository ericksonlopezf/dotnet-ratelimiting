// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class MutationKillAdversarialTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ConcurrencyPartition_RetiredAndIdleLifecycle_KillsMutants()
    {
        var partition = new ConcurrencyPartition(2);

        // 1. Initial idle state
        partition.IsIdle().Should().BeTrue();
        partition.IsRetired.Should().BeFalse();

        // 2. Acquire permit -> no longer idle
        partition.TryAcquire(1, out var rem).Should().BeTrue();
        rem.Should().Be(1);
        partition.IsIdle().Should().BeFalse();

        // 3. TryRetire fails while active permits > 0
        partition.TryRetire().Should().BeFalse();
        partition.IsRetired.Should().BeFalse();

        // 4. Release permit -> idle again
        partition.Release(1);
        partition.IsIdle().Should().BeTrue();

        // 5. TryRetire succeeds when 0 active permits
        partition.TryRetire().Should().BeTrue();
        partition.IsRetired.Should().BeTrue();

        // 6. TryAcquire fails on retired partition
        partition.TryAcquire(1, out var rem2).Should().BeFalse();
        rem2.Should().Be(0);

        // 7. TryAcquireEx returns Retired with remaining 0
        var exResult = partition.TryAcquireEx(1, out var remEx);
        exResult.Should().Be(ConcurrencyAcquireResult.Retired);
        remEx.Should().Be(0);

        // 8. Release on retired partition hits current == _retiredState break
        var actReleaseRetired = () => partition.Release(1);
        actReleaseRetired.Should().NotThrow();
    }

    [Fact]
    public async Task ConcurrencyRateLimiter_MaxPartitionsAndPruning_KillsMutants()
    {
        var options = new ConcurrencyRateLimiterOptions
        {
            PermitLimit = 5,
            MaxPartitions = 1
        };
        var limiter = new ConcurrencyRateLimiter(options);

        // Acquire partition 1
        var lease1 = await limiter.AcquireAsync("tenant-1", 1);
        lease1.Value.IsAcquired.Should().BeTrue();

        // Tenant 2 rejected because Count >= MaxPartitions and tenant-1 is active (not idle)
        var lease2 = await limiter.AcquireAsync("tenant-2", 1);
        lease2.Value.IsAcquired.Should().BeFalse();
        lease2.Value.RemainingPermits.Should().Be(0);

        // Dispose lease 1 -> tenant 1 is now idle
        lease1.Value.Dispose();

        // Tenant 2 now prunes tenant 1 and succeeds
        var lease2Retry = await limiter.AcquireAsync("tenant-2", 1);
        lease2Retry.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void ConcurrencyRateLimiterOptions_BoundaryValidations()
    {
        var options = new ConcurrencyRateLimiterOptions();

        options.PermitLimit = 1;
        options.PermitLimit.Should().Be(1);
        Action actLimitZero = () => options.PermitLimit = 0;
        actLimitZero.Should().Throw<ArgumentOutOfRangeException>();

        options.MaxPartitions = 1;
        options.MaxPartitions.Should().Be(1);
        Action actPartitionsZero = () => options.MaxPartitions = 0;
        actPartitionsZero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task FixedWindowRateLimiter_MaxPartitionsAndPruning_KillsMutants()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromSeconds(10),
            MaxPartitions = 1
        };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        // Acquire for key1
        var res1 = await limiter.AcquireAsync("client-1", 1);
        res1.Value.IsAcquired.Should().BeTrue();

        // Key2 rejected because Count >= MaxPartitions and key1 is active in current window
        var res2 = await limiter.AcquireAsync("client-2", 1);
        res2.Value.IsAcquired.Should().BeFalse();

        // Advance 25s so client-1 becomes idle (> 2 windows past)
        _timeProvider.Advance(TimeSpan.FromSeconds(25));

        // Client-2 now prunes client-1 and succeeds
        var res2Retry = await limiter.AcquireAsync("client-2", 1);
        res2Retry.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void FixedWindowPartition_BoundaryAndIdle_KillsMutants()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var partition = new FixedWindowPartition(2, TimeSpan.FromSeconds(10), start);

        // Acquire 2 at start
        var l1 = partition.TryAcquire(2, start);
        l1.IsAcquired.Should().BeTrue();

        // Rejection at same time
        var l2 = partition.TryAcquire(1, start);
        l2.IsAcquired.Should().BeFalse();
        l2.RetryAfter.Should().Be(TimeSpan.FromSeconds(10));

        // Exactly at window boundary (nextWindowTicks == now.UtcTicks)
        var exactBoundary = start.AddSeconds(10);
        // At exact boundary, window advances, count resets to 0!
        var l3 = partition.TryAcquire(2, exactBoundary);
        l3.IsAcquired.Should().BeTrue();

        // IsIdle tests
        partition.IsIdle(start).Should().BeFalse();
        partition.IsIdle(start.AddSeconds(10)).Should().BeFalse();
        partition.IsIdle(start.AddSeconds(40)).Should().BeTrue();
    }

    [Fact]
    public async Task SlidingWindowRateLimiter_MaxPartitionsAndPruning_KillsMutants()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromSeconds(10),
            SegmentsPerWindow = 5,
            MaxPartitions = 1
        };
        var limiter = new SlidingWindowRateLimiter(options, _timeProvider);

        var res1 = await limiter.AcquireAsync("client-1", 1);
        res1.Value.IsAcquired.Should().BeTrue();

        // Rejection due to MaxPartitions
        var res2 = await limiter.AcquireAsync("client-2", 1);
        res2.Value.IsAcquired.Should().BeFalse();

        // Advance 25s so client-1 is idle
        _timeProvider.Advance(TimeSpan.FromSeconds(25));

        var res2Retry = await limiter.AcquireAsync("client-2", 1);
        res2Retry.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void SlidingWindowPartition_OversizedRequestAndIdle_KillsMutants()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // limit 10, window 10s, 10 segments (1s each)
        var partition = new SlidingWindowPartition(10, TimeSpan.FromSeconds(10), 10, start);

        // Request 15 permits when limit is 10: permits > _permitLimit
        var leaseOversized = partition.TryAcquire(15, start);
        leaseOversized.IsAcquired.Should().BeFalse();
        leaseOversized.RetryAfter.Should().Be(TimeSpan.FromSeconds(10));

        // Acquire 8 permits
        var lease8 = partition.TryAcquire(8, start);
        lease8.IsAcquired.Should().BeTrue();
        lease8.RemainingPermits.Should().Be(2);

        // Request 5 permits (neededPermits = 5 - (10 - 8) = 3)
        var lease5 = partition.TryAcquire(5, start);
        lease5.IsAcquired.Should().BeFalse();

        // Test retryAfterTicks <= 0 fallback branch
        var windowEnd = start.AddSeconds(10);
        var leaseAtExpiry = partition.TryAcquire(10, windowEnd);
        leaseAtExpiry.IsAcquired.Should().BeTrue();

        // Monotonic time test: clock skew backward
        var backwardTime = start.AddSeconds(-5);
        var leaseBackward = partition.TryAcquire(1, backwardTime);
        leaseBackward.Should().NotBeNull();

        // IsIdle tests
        partition.IsIdle(start).Should().BeFalse();
        partition.IsIdle(start.AddSeconds(5)).Should().BeFalse();
        partition.IsIdle(start.AddSeconds(30)).Should().BeTrue();
    }

    [Fact]
    public async Task TokenBucketRateLimiter_MaxPartitionsAndPruning_KillsMutants()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromSeconds(10),
            MaxPartitions = 1
        };
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        // Consume all tokens so client-1 is not idle (idle requires full capacity)
        var res1 = await limiter.AcquireAsync("client-1", 5);
        res1.Value.IsAcquired.Should().BeTrue();

        // Client-2 rejected due to MaxPartitions
        var res2 = await limiter.AcquireAsync("client-2", 1);
        res2.Value.IsAcquired.Should().BeFalse();

        // Advance 20s so client-1 refills completely to full capacity and becomes idle
        _timeProvider.Advance(TimeSpan.FromSeconds(20));

        var res2Retry = await limiter.AcquireAsync("client-2", 1);
        res2Retry.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void TokenBucketPartition_OversizedRequestAndClamp_KillsMutants()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // capacity 10, window 10s -> refill 1/s
        var partition = new TokenBucketPartition(10, TimeSpan.FromSeconds(10), start);

        // Request 15 tokens: permits > _capacity -> missingTokens = _capacity = 10
        var leaseOversized = partition.TryAcquire(15, start);
        leaseOversized.IsAcquired.Should().BeFalse();
        leaseOversized.RetryAfter!.Value.TotalSeconds.Should().BeApproximately(10.0, 0.01);

        // Partition with very slow refill (1 token per 200,000s) to trigger 86400s clamp
        var slowPartition = new TokenBucketPartition(1, TimeSpan.FromSeconds(200_000), start);
        slowPartition.TryAcquire(1, start); // consume token
        var clampedLease = slowPartition.TryAcquire(1, start);
        clampedLease.IsAcquired.Should().BeFalse();
        clampedLease.RetryAfter!.Value.TotalSeconds.Should().Be(86400.0);

        // IsIdle checks
        var pIdle = new TokenBucketPartition(5, TimeSpan.FromSeconds(5), start);
        pIdle.IsIdle(start).Should().BeTrue(); // full
        pIdle.TryAcquire(5, start);
        pIdle.IsIdle(start).Should().BeFalse(); // empty
        pIdle.IsIdle(start.AddSeconds(2)).Should().BeFalse(); // partial
        pIdle.IsIdle(start.AddSeconds(10)).Should().BeTrue(); // refilled
    }

    [Fact]
    public async Task CompositeRateLimiter_AggregateMetricsAndRollback_KillsMutants()
    {
        // Limiter 1: Limit 100, window 60s
        var lim1 = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromSeconds(60)
        }, _timeProvider);

        // Limiter 2: Limit 50, window 10s
        var lim2 = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = 50,
            Window = TimeSpan.FromSeconds(10)
        }, _timeProvider);

        var composite = new CompositeRateLimiter(lim1, lim2);
        var result = await composite.AcquireAsync("test-key", 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(49);
        result.Value.Limit.Should().Be(50);

        // Rollback test: child 1 succeeds (concurrency), child 2 rejects (limit 1)
        var concLimiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
        {
            PermitLimit = 10
        });

        var strictLimiter = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromSeconds(60)
        }, _timeProvider);

        // Exhaust strict limiter first
        (await strictLimiter.AcquireAsync("roll-key", 1)).Value.IsAcquired.Should().BeTrue();

        var compositeRej = new CompositeRateLimiter(concLimiter, strictLimiter);
        var resRej = await compositeRej.AcquireAsync("roll-key", 1);

        resRej.IsSuccess.Should().BeTrue();
        resRej.Value.IsAcquired.Should().BeFalse();

        // Concurrency limiter lease from first child was rolled back, so it still has full 10 permits:
        var concCheck = await concLimiter.AcquireAsync("roll-key", 10);
        concCheck.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void ConcurrencyPartition_Release_WhenRetired_PreservesRetiredState()
    {
        var partition = new ConcurrencyPartition(5);
        partition.TryRetire().Should().BeTrue();
        partition.IsRetired.Should().BeTrue();

        partition.Release(1);

        partition.IsRetired.Should().BeTrue();
        partition.TryAcquireEx(1, out _).Should().Be(ConcurrencyAcquireResult.Retired);
    }

    [Fact]
    public async Task ConcurrencyRateLimiter_WhenPartitionInDictionaryIsRetired_ReplacesWithNewPartition()
    {
        var options = new ConcurrencyRateLimiterOptions
        {
            PermitLimit = 2,
            MaxPartitions = 10
        };
        var limiter = new ConcurrencyRateLimiter(options);

        var partitionsField = typeof(ConcurrencyRateLimiter).GetField("_partitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var partitions = (System.Collections.Concurrent.ConcurrentDictionary<string, ConcurrencyPartition>)partitionsField.GetValue(limiter)!;

        var retiredPartition = new ConcurrencyPartition(2);
        retiredPartition.TryRetire();
        partitions["stale-key"] = retiredPartition;

        var leaseResult = await limiter.AcquireAsync("stale-key", 1);
        leaseResult.IsSuccess.Should().BeTrue();
        leaseResult.Value.IsAcquired.Should().BeTrue();
        partitions["stale-key"].Should().NotBeSameAs(retiredPartition);
    }

    [Fact]
    public void FixedWindowPartition_IsIdleBoundary_KillsMutant()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var window = TimeSpan.FromSeconds(10);
        var partition = new FixedWindowPartition(5, window, t0);

        partition.TryAcquire(1, t0).IsAcquired.Should().BeTrue();

        // Exactly at window 1 (lastWindowIndex + 1) -> not idle
        partition.IsIdle(t0 + window).Should().BeFalse();

        // At window 2 (lastWindowIndex + 2) -> idle
        partition.IsIdle(t0 + (window * 2)).Should().BeTrue();
    }

    [Fact]
    public void SlidingWindowPartition_AdvanceAndResetTime_KillsMutants()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var interval = TimeSpan.FromSeconds(10);
        // 3 segments of 10s each = 30s window, limit 10
        var partition = new SlidingWindowPartition(10, TimeSpan.FromSeconds(30), 3, t0);

        // Segment 0: acquire 3
        var lease1 = partition.TryAcquire(3, t0);
        lease1.IsAcquired.Should().BeTrue();
        lease1.ResetTime.Should().Be(t0.AddSeconds(10));

        // Segment 1 (t0 + 10s): acquire 5
        var t1 = t0.AddSeconds(10);
        var lease2 = partition.TryAcquire(5, t1);
        lease2.IsAcquired.Should().BeTrue();
        lease2.ResetTime.Should().Be(t0.AddSeconds(20));

        // Segment 2 (t0 + 20s): acquire 1
        var t2 = t0.AddSeconds(20);
        partition.TryAcquire(1, t2).IsAcquired.Should().BeTrue();
        // Total usage is 3 + 5 + 1 = 9 permits. Remaining = 1.

        // Advance by 1 segment to segment 3 (t0 + 30s): segment 0 expires (3 permits freed).
        // Usage should now be 5 + 1 = 6. Requesting 3 should succeed (6 + 3 = 9 <= 10).
        var t3 = t0.AddSeconds(30);
        var lease3 = partition.TryAcquire(3, t3);
        lease3.IsAcquired.Should().BeTrue();
        lease3.RemainingPermits.Should().Be(1);

        // Exact neededPermits == freed test:
        // Current usage: seg 1 (5), seg 2 (1), seg 3 (3) = 9 permits.
        // Request 2 permits: neededPermits = 2 - (10 - 9) = 1.
        // Earliest expiring segment is seg 1 (which has 5 permits >= 1).
        // Seg 1 expires at (1 + 3) * 10s = 40s (i.e. t0 + 40s).
        // Since current time is t3 (30s), RetryAfter must be exactly 10s!
        var leaseRej = partition.TryAcquire(2, t3);
        leaseRej.IsAcquired.Should().BeFalse();
        leaseRej.RetryAfter.Should().Be(TimeSpan.FromSeconds(10));

        // IsIdle boundary test:
        // lastSegmentIndex is now 3.
        // currentSegmentIndex - 3 == 3 (i.e. segment 6, t0 + 60s) -> not idle (> 3 is false)
        partition.IsIdle(t0.AddSeconds(60)).Should().BeFalse();
        // currentSegmentIndex - 3 == 4 (i.e. segment 7, t0 + 70s) -> idle (> 3 is true)
        partition.IsIdle(t0.AddSeconds(70)).Should().BeTrue();
    }

    [Fact]
    public void SlidingWindowPartition_PermitsEqualsLimitAndBoundaryFreed_KillsMutants()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // 3 segments of 10s = 30s window, limit 10
        var partition = new SlidingWindowPartition(10, TimeSpan.FromSeconds(30), 3, t0);

        // Segment 0: 1 permit
        partition.TryAcquire(1, t0).IsAcquired.Should().BeTrue();

        // Segment 1 (t0 + 10s): 7 permits
        var t1 = t0.AddSeconds(10);
        partition.TryAcquire(7, t1).IsAcquired.Should().BeTrue();
        // Current usage is 1 + 7 = 8.

        // Test 1: Request 3 permits.
        // neededPermits = 3 - (10 - 8) = 1.
        // Seg 0 has EXACTLY 1 permit. freed = 1.
        // With >=, 1 >= 1 is true: earliest expiry is Seg 0 (t0 + 30s).
        // Since current time is t1 (10s), RetryAfter is 30s - 10s = 20s.
        var lease1 = partition.TryAcquire(3, t1);
        lease1.IsAcquired.Should().BeFalse();
        lease1.RetryAfter.Should().Be(TimeSpan.FromSeconds(20));

        // Test 2: Request 10 permits (permits == _permitLimit).
        // If permits < _permitLimit mutation, loop is skipped and fallback full-window is returned!
        // With permits <= _permitLimit, neededPermits = 10 - (10 - 8) = 8.
        // Seg 0 (1 permit) + Seg 1 (7 permits) = 8 permits >= 8.
        // Expiry of Seg 1 is t0 + 40s. RetryAfter = 40s - 10s = 30s.
        var lease2 = partition.TryAcquire(10, t1);
        lease2.IsAcquired.Should().BeFalse();
        lease2.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TokenBucketPartition_RefillArithmeticAndResetTime_KillsMutants()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // capacity 10, window 5s -> refillRate 2.0 tokens/sec
        var partition = new TokenBucketPartition(10, TimeSpan.FromSeconds(5), t0);

        // 1. Acquire 6 tokens: 4 tokens remaining, 6 missing to capacity.
        // secondsToFull = 6 / 2 = 3.0 seconds. ResetTime must be t0 + 3s!
        var lease1 = partition.TryAcquire(6, t0);
        lease1.IsAcquired.Should().BeTrue();
        lease1.RemainingPermits.Should().Be(4);
        lease1.ResetTime.Should().Be(t0.AddSeconds(3.0));

        // 2. Reject when permits == capacity (10 tokens requested with 4 remaining).
        // permits > capacity is false -> missingTokens = 10 - 4 = 6.
        // secondsToWait = 6 / 2 = 3.0 seconds.
        var lease2 = partition.TryAcquire(10, t0);
        lease2.IsAcquired.Should().BeFalse();
        lease2.RetryAfter.Should().Be(TimeSpan.FromSeconds(3.0));

        // 3. IsIdle with refill rate 10.0:
        var fastPartition = new TokenBucketPartition(10, TimeSpan.FromSeconds(1), t0);
        fastPartition.TryAcquire(10, t0); // empty (0 tokens)
        // At 0.5s: 0.5 * 10 = 5 tokens < 10 -> not idle
        fastPartition.IsIdle(t0.AddSeconds(0.5)).Should().BeFalse();
        // At 1.0s: 1.0 * 10 = 10 tokens == 10 -> idle
        fastPartition.IsIdle(t0.AddSeconds(1.0)).Should().BeTrue();
    }

    [Fact]
    public async Task CompositeRateLimiter_NullCoalescingAndLimits_KillsMutants()
    {
        // Test Case 1: Limiter 1 (Limit = 5, succeeds), Limiter 2 (Limit = null, rejects) -> rejected Limit is 5
        var limSuccess5 = new FakeRateLimiter(RateLimitLease.Successful(10, null, null, 5));
        var limRejectNull = new FakeRateLimiter(RateLimitLease.Rejected(TimeSpan.FromSeconds(1), null, null));
        var comp1 = new CompositeRateLimiter(limSuccess5, limRejectNull);
        var res1 = await comp1.AcquireAsync("key", 1);
        res1.Value.IsAcquired.Should().BeFalse();
        res1.Value.Limit.Should().Be(5);

        // Test Case 2: Limiter 1 (Limit = 5, succeeds), Limiter 2 (Limit = 10, rejects) -> rejected Limit is 10
        var limReject10 = new FakeRateLimiter(RateLimitLease.Rejected(TimeSpan.FromSeconds(1), null, 10));
        var comp2 = new CompositeRateLimiter(limSuccess5, limReject10);
        var res2 = await comp2.AcquireAsync("key", 1);
        res2.Value.IsAcquired.Should().BeFalse();
        res2.Value.Limit.Should().Be(10);

        // Test Case 3: Limiter 1 (Limit = null, succeeds), Limiter 2 (Limit = 8, rejects) -> rejected Limit is 8
        var limSuccessNull = new FakeRateLimiter(RateLimitLease.Successful(10, null, null, null));
        var limReject8 = new FakeRateLimiter(RateLimitLease.Rejected(TimeSpan.FromSeconds(1), null, 8));
        var comp3 = new CompositeRateLimiter(limSuccessNull, limReject8);
        var res3 = await comp3.AcquireAsync("key", 1);
        res3.Value.IsAcquired.Should().BeFalse();
        res3.Value.Limit.Should().Be(8);

        // Test Case 4: Multiple successes with null and ordered limits & reset times
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 10, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 1, 0, 0, 20, TimeSpan.Zero);
        var sNull = new FakeRateLimiter(RateLimitLease.Successful(20, null, null, null));
        var s10 = new FakeRateLimiter(RateLimitLease.Successful(15, t1, null, 10));
        var s5 = new FakeRateLimiter(RateLimitLease.Successful(5, t2, null, 5));

        var compSuccess = new CompositeRateLimiter(sNull, s10, s5);
        var resSuccess = await compSuccess.AcquireAsync("key", 1);
        resSuccess.Value.IsAcquired.Should().BeTrue();
        resSuccess.Value.RemainingPermits.Should().Be(5);
        resSuccess.Value.Limit.Should().Be(5);
        resSuccess.Value.ResetTime.Should().Be(t2);

        // Test Case 5: When null lease comes after non-null lease
        var compNullLast = new CompositeRateLimiter(s5, sNull);
        var resNullLast = await compNullLast.AcquireAsync("key", 1);
        resNullLast.Value.Limit.Should().Be(5);
        resNullLast.Value.ResetTime.Should().Be(t2);

        // Test Case 6: Decreasing reset times (t2 first, then t1)
        var sEarlier = new FakeRateLimiter(RateLimitLease.Successful(15, t1, null, 10));
        var sLater = new FakeRateLimiter(RateLimitLease.Successful(15, t2, null, 10));
        var compDecreasing = new CompositeRateLimiter(sLater, sEarlier);
        var resDecreasing = await compDecreasing.AcquireAsync("key", 1);
        resDecreasing.Value.ResetTime.Should().Be(t2);
    }

    [Fact]
    public async Task RateLimiter_GuardClauses_ThrowExpectedExceptions()
    {
        var options = new RateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromSeconds(10) };
        var concOptions = new ConcurrencyRateLimiterOptions { PermitLimit = 5 };

        var limiters = new IRateLimiter[]
        {
            new ConcurrencyRateLimiter(concOptions),
            new FixedWindowRateLimiter(options, _timeProvider),
            new SlidingWindowRateLimiter(options, _timeProvider),
            new TokenBucketRateLimiter(options, _timeProvider),
            new CompositeRateLimiter(new ConcurrencyRateLimiter(concOptions))
        };

        foreach (var limiter in limiters)
        {
            // Null key
            var actNullKey = () => limiter.AcquireAsync(null!, 1);
            await actNullKey.Should().ThrowAsync<ArgumentNullException>();

            // Less than 1 permits
            var actZeroPermits = () => limiter.AcquireAsync("key", 0);
            await actZeroPermits.Should().ThrowAsync<ArgumentOutOfRangeException>();

            var actNegPermits = () => limiter.AcquireAsync("key", -1);
            await actNegPermits.Should().ThrowAsync<ArgumentOutOfRangeException>();

            // Canceled token
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();
            var actCanceled = () => limiter.AcquireAsync("key", 1, cts.Token);
            await actCanceled.Should().ThrowAsync<OperationCanceledException>();
        }

        // CompositeRateLimiter constructor with null
        Action actNullComp = () => _ = new CompositeRateLimiter((IRateLimiter[])null!);
        actNullComp.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RateLimiterPolicyBuilder_GuardClauses_ThrowExpectedExceptions()
    {
        var builder = new EricksonLopez.RateLimiting.Policies.RateLimiterPolicyBuilder();
        var fakeLimiter = new FakeRateLimiter(default);

        Action actNull1 = () => builder.AddPolicy(null!, fakeLimiter);
        actNull1.Should().Throw<ArgumentException>();
        Action actEmpty1 = () => builder.AddPolicy("  ", fakeLimiter);
        actEmpty1.Should().Throw<ArgumentException>();
        Action actNullConfig1 = () => builder.AddPolicy("test", (IRateLimiter)null!);
        actNullConfig1.Should().Throw<ArgumentNullException>();

        Action actNull2 = () => builder.AddConcurrency(null!, _ => { });
        actNull2.Should().Throw<ArgumentException>();
        Action actEmpty2 = () => builder.AddConcurrency("", _ => { });
        actEmpty2.Should().Throw<ArgumentException>();
        Action actNullConfig2 = () => builder.AddConcurrency("test", null!);
        actNullConfig2.Should().Throw<ArgumentNullException>();

        Action actNull3 = () => builder.AddFixedWindow(null!, _ => { });
        actNull3.Should().Throw<ArgumentException>();
        Action actEmpty3 = () => builder.AddFixedWindow("", _ => { });
        actEmpty3.Should().Throw<ArgumentException>();
        Action actNullConfig3 = () => builder.AddFixedWindow("test", null!);
        actNullConfig3.Should().Throw<ArgumentNullException>();

        Action actNull4 = () => builder.AddSlidingWindow(null!, _ => { });
        actNull4.Should().Throw<ArgumentException>();
        Action actEmpty4 = () => builder.AddSlidingWindow("", _ => { });
        actEmpty4.Should().Throw<ArgumentException>();
        Action actNullConfig4 = () => builder.AddSlidingWindow("test", null!);
        actNullConfig4.Should().Throw<ArgumentNullException>();

        Action actNull5 = () => builder.AddTokenBucket(null!, _ => { });
        actNull5.Should().Throw<ArgumentException>();
        Action actEmpty5 = () => builder.AddTokenBucket("", _ => { });
        actEmpty5.Should().Throw<ArgumentException>();
        Action actNullConfig5 = () => builder.AddTokenBucket("test", null!);
        actNullConfig5.Should().Throw<ArgumentNullException>();

        Action actNull6 = () => builder.AddComposite(null!, fakeLimiter);
        actNull6.Should().Throw<ArgumentException>();
        Action actEmpty6 = () => builder.AddComposite("   ", fakeLimiter);
        actEmpty6.Should().Throw<ArgumentException>();
        Action actNullConfig6 = () => builder.AddComposite("test", (IRateLimiter[])null!);
        actNullConfig6.Should().Throw<ArgumentNullException>();

        Action actNullDefault = () => builder.SetDefaultPolicy((string)null!);
        actNullDefault.Should().Throw<ArgumentException>();
        Action actNullDefaultLimiter = () => builder.SetDefaultPolicy((IRateLimiter)null!);
        actNullDefaultLimiter.Should().Throw<ArgumentNullException>();
        Action actEmptyDefault = () => builder.SetDefaultPolicy("   ");
        actEmptyDefault.Should().Throw<ArgumentException>();
        Action actUnregistered = () => builder.SetDefaultPolicy("nonexistent");
        actUnregistered.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RateLimitingServiceCollectionExtensions_GuardClauses_ThrowExpectedExceptions()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        Action act1 = () => RateLimitingServiceCollectionExtensions.AddConcurrencyRateLimiter(null!);
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => RateLimitingServiceCollectionExtensions.AddFixedWindowRateLimiter(null!);
        act2.Should().Throw<ArgumentNullException>();

        Action act3 = () => RateLimitingServiceCollectionExtensions.AddSlidingWindowRateLimiter(null!);
        act3.Should().Throw<ArgumentNullException>();

        Action act4 = () => RateLimitingServiceCollectionExtensions.AddTokenBucketRateLimiter(null!);
        act4.Should().Throw<ArgumentNullException>();

        Action act5 = () => RateLimitingServiceCollectionExtensions.AddCompositeRateLimiter(null!, new FakeRateLimiter(default));
        act5.Should().Throw<ArgumentNullException>();

        Action act6 = () => RateLimitingServiceCollectionExtensions.AddCompositeRateLimiter(services, (IRateLimiter[])null!);
        act6.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Options_ValidationMessages_KillsStringMutants()
    {
        var opt = new RateLimiterOptions();
        Action actLimit = () => opt.PermitLimit = 0;
        actLimit.Should().Throw<ArgumentOutOfRangeException>();

        Action actSegments = () => opt.SegmentsPerWindow = 0;
        actSegments.Should().Throw<ArgumentOutOfRangeException>();

        Action actMaxPartitions = () => opt.MaxPartitions = 0;
        actMaxPartitions.Should().Throw<ArgumentOutOfRangeException>();

        Action actWindowZero = () => opt.Window = TimeSpan.Zero;
        actWindowZero.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Window must be greater than zero.*");

        Action actWindowNeg = () => opt.Window = TimeSpan.FromSeconds(-5);
        actWindowNeg.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Window must be greater than zero.*");

        var concOpt = new ConcurrencyRateLimiterOptions();
        Action actConcLimit = () => concOpt.PermitLimit = 0;
        actConcLimit.Should().Throw<ArgumentOutOfRangeException>();

        Action actConcMaxPartitions = () => concOpt.MaxPartitions = 0;
        actConcMaxPartitions.Should().Throw<ArgumentOutOfRangeException>();

        RateLimitingMetrics.RequestsTotal.Unit.Should().Be("{request}");
        RateLimitingMetrics.RequestsTotal.Description.Should().Be("Total number of rate limit permit evaluation attempts.");
        RateLimitingMetrics.LeaseDuration.Unit.Should().Be("ms");
        RateLimitingMetrics.LeaseDuration.Description.Should().Be("Duration of rate limit permit acquisition attempt.");
    }

    [Fact]
    public async Task CompositeRateLimiter_HasDisposeAction_WhenNoChildrenHaveDispose_ReturnsNullDisposeAction()
    {
        var lim1 = new FakeRateLimiter(RateLimitLease.Successful(10, (DateTimeOffset?)null, (Action?)null));
        var lim2 = new FakeRateLimiter(RateLimitLease.Successful(10, (DateTimeOffset?)null, (Action?)null));
        var comp = new CompositeRateLimiter(lim1, lim2);

        var res = await comp.AcquireAsync("key", 1);
        res.Value.IsAcquired.Should().BeTrue();
        res.Value.DisposeAction.Should().BeNull();
    }

    private sealed class FakeRateLimiter : IRateLimiter
    {
        private readonly RateLimitLease _lease;
        public FakeRateLimiter(RateLimitLease lease) => _lease = lease;
        public Task<EricksonLopez.Result.Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult(EricksonLopez.Result.Result<RateLimitLease>.Success(_lease));
    }
}
