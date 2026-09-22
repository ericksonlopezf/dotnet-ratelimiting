// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class InvariantMathematicalTests
{
    private readonly FakeTimeProvider _timeProvider;

    public InvariantMathematicalTests()
    {
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task TokenBucket_Replenishment_ExactMath_NeverExceedsCapacity()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(10) // 1 token per second
        };
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        // Consume all 10 tokens
        var r1 = await limiter.AcquireAsync("k1", 10);
        r1.Value.IsAcquired.Should().BeTrue();
        r1.Value.RemainingPermits.Should().Be(0);

        // Advance 100 seconds (10x window)
        _timeProvider.Advance(TimeSpan.FromSeconds(100));

        // Tokens must be clamped strictly to capacity (10), not 100!
        var r2 = await limiter.AcquireAsync("k1", 1);
        r2.Value.IsAcquired.Should().BeTrue();
        r2.Value.RemainingPermits.Should().Be(9);

        // Now acquire remaining 9 tokens
        var r3 = await limiter.AcquireAsync("k1", 9);
        r3.Value.IsAcquired.Should().BeTrue();
        r3.Value.RemainingPermits.Should().Be(0);

        // Now 11th request must be rejected
        var r4 = await limiter.AcquireAsync("k1", 1);
        r4.Value.IsAcquired.Should().BeFalse();
        r4.Value.RemainingPermits.Should().Be(0);
        r4.Value.RetryAfter.Should().NotBeNull();
        r4.Value.RetryAfter!.Value.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task TokenBucket_FractionalRefill_ComputesCorrectRetryAfter()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(10) // 1 token per second
        };
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        await limiter.AcquireAsync("k2", 10);

        // Advance 500ms (0.5 tokens replenished)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(500));

        // Request 1 token -> needed is 0.5 -> Math.Ceiling(0.5) is 1 token interval -> 1 second
        var r = await limiter.AcquireAsync("k2", 1);
        r.Value.IsAcquired.Should().BeFalse();
        r.Value.RetryAfter.Should().NotBeNull();
        // Wait time for 1 full permit
        r.Value.RetryAfter!.Value.TotalSeconds.Should().BeGreaterThan(0);
        r.Value.RetryAfter!.Value.TotalSeconds.Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public async Task SlidingWindow_SegmentRotation_ClearsOldSegmentsPrecisely()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(10),
            SegmentsPerWindow = 10 // 1 second per segment
        };
        var limiter = new SlidingWindowRateLimiter(options, _timeProvider);

        // Acquire 5 in segment 0
        var r1 = await limiter.AcquireAsync("k3", 5);
        r1.Value.IsAcquired.Should().BeTrue();

        // Advance 3 seconds (segment 3) and acquire 5
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        var r2 = await limiter.AcquireAsync("k3", 5);
        r2.Value.IsAcquired.Should().BeTrue();

        // Total usage is 10/10 -> rejected
        var r3 = await limiter.AcquireAsync("k3", 1);
        r3.Value.IsAcquired.Should().BeFalse();

        // Advance 7.1 seconds (total 10.1s since t0). Segment 0 should have rolled out!
        _timeProvider.Advance(TimeSpan.FromMilliseconds(7100));

        // Segment 0's 5 permits expired, leaving only segment 3's 5 permits
        var r4 = await limiter.AcquireAsync("k3", 5);
        r4.Value.IsAcquired.Should().BeTrue();
        r4.Value.RemainingPermits.Should().Be(0);

        // 6th permit should be rejected
        var r5 = await limiter.AcquireAsync("k3", 1);
        r5.Value.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task FixedWindow_BoundaryTransition_ResetsWindowCleanly()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromSeconds(10)
        };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        var r1 = await limiter.AcquireAsync("k4", 5);
        r1.Value.IsAcquired.Should().BeTrue();
        r1.Value.RemainingPermits.Should().Be(0);

        // Exactly at window boundary - 1ms
        _timeProvider.Advance(TimeSpan.FromMilliseconds(9999));
        var r2 = await limiter.AcquireAsync("k4", 1);
        r2.Value.IsAcquired.Should().BeFalse();

        // Cross the boundary by 2ms (total 10.001s)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(2));
        var r3 = await limiter.AcquireAsync("k4", 5);
        r3.Value.IsAcquired.Should().BeTrue();
        r3.Value.RemainingPermits.Should().Be(0);
    }

    [Fact]
    public async Task PartitionPruning_RemovesIdlePartitions_UnderHighCardinality()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromSeconds(1),
            MaxPartitions = 50
        };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        // Create 40 partitions
        for (int i = 0; i < 40; i++)
        {
            await limiter.AcquireAsync($"user-{i}", 1);
        }

        // Advance past window to make them idle
        _timeProvider.Advance(TimeSpan.FromSeconds(5));

        // Create another 20 partitions -> total requested exceeds MaxPartitions -> triggers pruning
        for (int i = 40; i < 60; i++)
        {
            await limiter.AcquireAsync($"user-{i}", 1);
        }

        // The limiter must remain operational and not crash or leak memory
        var check = await limiter.AcquireAsync("new-user", 1);
        check.Value.IsAcquired.Should().BeTrue();
    }
}
