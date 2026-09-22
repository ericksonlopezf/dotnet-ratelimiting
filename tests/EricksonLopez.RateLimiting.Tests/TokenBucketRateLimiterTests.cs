// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class TokenBucketRateLimiterTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task AcquireAsync_ConsumesTokensUntilExhausted()
    {
        var options = new RateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromSeconds(10) };
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        var first = await limiter.AcquireAsync("tenant-1", 6);
        first.Value.IsAcquired.Should().BeTrue();
        first.Value.RemainingPermits.Should().Be(4);

        var second = await limiter.AcquireAsync("tenant-1", 4);
        second.Value.IsAcquired.Should().BeTrue();

        var third = await limiter.AcquireAsync("tenant-1", 1);
        third.Value.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task AcquireAsync_ReplenishesTokensContinuously()
    {
        var options = new RateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromSeconds(10) }; // 1 token per second
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        // Exhaust all 10 tokens
        await limiter.AcquireAsync("tenant-2", 10);
        (await limiter.AcquireAsync("tenant-2", 1)).Value.IsAcquired.Should().BeFalse();

        // Advance 3 seconds -> replenishes 3 tokens
        _timeProvider.Advance(TimeSpan.FromSeconds(3));

        var replenishResult = await limiter.AcquireAsync("tenant-2", 2);
        replenishResult.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_ReplenishmentDoesNotExceedCapacity()
    {
        var options = new RateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromSeconds(5) }; // 1 token per second
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        // Advance 100 seconds without consuming anything
        _timeProvider.Advance(TimeSpan.FromSeconds(100));

        // Capacity is 5, consuming 5 must succeed, but 6th must fail
        var five = await limiter.AcquireAsync("tenant-clamp", 5);
        five.Value.IsAcquired.Should().BeTrue();
        five.Value.RemainingPermits.Should().Be(0);

        var sixth = await limiter.AcquireAsync("tenant-clamp", 1);
        sixth.Value.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task AcquireAsync_NullKey_ThrowsArgumentNullException()
    {
        var limiter = new TokenBucketRateLimiter();

        Func<Task> act = () => limiter.AcquireAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DefaultConstructor_UsesDefaultOptionsAndSystemTimeProvider()
    {
        var limiter = new TokenBucketRateLimiter();

        var result = await limiter.AcquireAsync("default-token-client", 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(99);
    }

    [Fact]
    public async Task AcquireAsync_DifferentPartitions_AreIsolated()
    {
        var options = new RateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromSeconds(10) };
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        var resA = await limiter.AcquireAsync("partition-A", 1);
        var resB = await limiter.AcquireAsync("partition-B", 1);

        resA.Value.IsAcquired.Should().BeTrue();
        resB.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void TokenBucketPartition_ReplenishmentAndRejectionCalculation_Accurate()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // capacity 10, window 5s -> refill rate = 2.0 tokens/s
        var partition = new TokenBucketPartition(10, TimeSpan.FromSeconds(5), startTime);

        // Consume 9 tokens
        var lease1 = partition.TryAcquire(9, startTime);
        lease1.IsAcquired.Should().BeTrue();
        lease1.RemainingPermits.Should().Be(1);

        // Request 4 tokens at same time -> missing = 4 - 1 = 3 -> 3 / 2 = 1.5s
        var lease2 = partition.TryAcquire(4, startTime);
        lease2.IsAcquired.Should().BeFalse();
        lease2.RetryAfter!.Value.TotalSeconds.Should().BeApproximately(1.5, 0.001);

        // Advance 2s: adds 2 * 2 = 4 tokens -> current = 1 + 4 = 5
        var t2 = startTime.AddSeconds(2);
        var lease3 = partition.TryAcquire(4, t2);
        lease3.IsAcquired.Should().BeTrue();
        lease3.RemainingPermits.Should().Be(1);
    }
}

