// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class SlidingWindowRateLimiterTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task AcquireAsync_UnderLimit_AcquiresSuccessfully()
    {
        var options = new RateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) };
        var limiter = new SlidingWindowRateLimiter(options, _timeProvider);

        for (var i = 1; i <= 5; i++)
        {
            var result = await limiter.AcquireAsync("client-ip-1");
            result.IsSuccess.Should().BeTrue();
            result.Value.IsAcquired.Should().BeTrue();
            result.Value.RemainingPermits.Should().Be(5 - i);
        }
    }

    [Fact]
    public async Task AcquireAsync_ExceedingLimit_RejectsWithRetryAfter()
    {
        var options = new RateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 };
        var limiter = new SlidingWindowRateLimiter(options, _timeProvider);

        // Advance 3 seconds into the 10-second segment
        _timeProvider.Advance(TimeSpan.FromSeconds(3));

        await limiter.AcquireAsync("client-ip-2", 3);
        var rejected = await limiter.AcquireAsync("client-ip-2", 1);

        rejected.IsSuccess.Should().BeTrue();
        rejected.Value.IsAcquired.Should().BeFalse();
        // All 3 permits were acquired in segment 0 (0..10s). In a 60s sliding window,
        // segment 0 only slides out at t = 60s. From t = 3s, exactly 57 seconds must elapse before permits free up.
        rejected.Value.RetryAfter!.Value.TotalSeconds.Should().BeApproximately(57.0, 0.1);
    }

    [Fact]
    public async Task AcquireAsync_AdvancingTime_RestoresPermits()
    {
        var options = new RateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 };
        var limiter = new SlidingWindowRateLimiter(options, _timeProvider);

        await limiter.AcquireAsync("client-ip-3", 2);
        (await limiter.AcquireAsync("client-ip-3")).Value.IsAcquired.Should().BeFalse();

        // Advance beyond window (65 seconds)
        _timeProvider.Advance(TimeSpan.FromSeconds(65));

        var freshResult = await limiter.AcquireAsync("client-ip-3");
        freshResult.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_PartialSegmentAdvance_ClearsOnlyExpiredSegments()
    {
        // 6 segments of 10s = 60s window
        var options = new RateLimiterOptions { PermitLimit = 4, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 };
        var limiter = new SlidingWindowRateLimiter(options, _timeProvider);

        // Consume 2 permits in segment 0 (t=0)
        await limiter.AcquireAsync("client-partial", 2);

        // Advance 10s to segment 1 (t=10s)
        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        // Consume 2 permits in segment 1 (t=10s) -> total 4 permits used
        await limiter.AcquireAsync("client-partial", 2);

        // Now limit is reached:
        (await limiter.AcquireAsync("client-partial", 1)).Value.IsAcquired.Should().BeFalse();

        // Advance 51s (from t=10s to t=61s). Segment 0 (t=0) is now expired (>60s ago), but segment 1 (at t=10s) is still within 60s window (t=61 - 10 = 51s < 60s)!
        // So 2 permits from segment 0 should have freed up, allowing 2 permits!
        _timeProvider.Advance(TimeSpan.FromSeconds(51));

        var acquireFreed = await limiter.AcquireAsync("client-partial", 2);
        acquireFreed.Value.IsAcquired.Should().BeTrue();
        acquireFreed.Value.RemainingPermits.Should().Be(0); // 2 active in segment 1 + 2 just acquired = 4
    }

    [Fact]
    public async Task AcquireAsync_NullKey_ThrowsArgumentNullException()
    {
        var limiter = new SlidingWindowRateLimiter();

        Func<Task> act = () => limiter.AcquireAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DefaultConstructor_UsesDefaultOptionsAndSystemTimeProvider()
    {
        var limiter = new SlidingWindowRateLimiter();

        var result = await limiter.AcquireAsync("default-client", 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(99);
    }

    [Fact]
    public async Task AcquireAsync_DifferentPartitions_AreIsolated()
    {
        var options = new RateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) };
        var limiter = new SlidingWindowRateLimiter(options, _timeProvider);

        var resA = await limiter.AcquireAsync("partition-A", 1);
        var resB = await limiter.AcquireAsync("partition-B", 1);

        resA.Value.IsAcquired.Should().BeTrue();
        resB.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void SlidingWindowPartition_AdvanceSegment_ClearsCorrectRingBufferIndex()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // 4 segments of 10s = 40s window, limit 4
        var partition = new SlidingWindowPartition(4, TimeSpan.FromSeconds(40), 4, startTime);

        // Acquire 2 at t=0 (segment 0)
        var l1 = partition.TryAcquire(2, startTime);
        l1.IsAcquired.Should().BeTrue();

        // Advance 10s to t=10 (segment 1). Should clear segment 1 and allow 2 more
        var t10 = startTime.AddSeconds(10);
        var l2 = partition.TryAcquire(2, t10);
        l2.IsAcquired.Should().BeTrue();

        // Now capacity is 4/4. Next request at t=10 must be rejected:
        var l3 = partition.TryAcquire(1, t10);
        l3.IsAcquired.Should().BeFalse();

        // Advance 30s to t=40 (segment 4 == segment 0 in ring buffer).
        // Segment 0 from t=0 should be cleared.
        var t40 = startTime.AddSeconds(40);
        var l4 = partition.TryAcquire(2, t40);
        l4.IsAcquired.Should().BeTrue();
    }
}

