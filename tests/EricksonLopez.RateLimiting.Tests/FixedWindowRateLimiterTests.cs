// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class FixedWindowRateLimiterTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task AcquireAsync_UnderLimit_AcquiresSuccessfully()
    {
        var options = new RateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        for (var i = 1; i <= 5; i++)
        {
            var result = await limiter.AcquireAsync("client-fixed-1");
            result.IsSuccess.Should().BeTrue();
            result.Value.IsAcquired.Should().BeTrue();
            result.Value.RemainingPermits.Should().Be(5 - i);
        }
    }

    [Fact]
    public async Task AcquireAsync_ExceedingLimit_RejectsWithRetryAfter()
    {
        var options = new RateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(1) };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        await limiter.AcquireAsync("client-fixed-2", 3);
        var rejected = await limiter.AcquireAsync("client-fixed-2", 1);

        rejected.IsSuccess.Should().BeTrue();
        rejected.Value.IsAcquired.Should().BeFalse();
        rejected.Value.RetryAfter.Should().NotBeNull();
        rejected.Value.RetryAfter.Value.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task AcquireAsync_AdvancingTimeToNextWindow_RestoresPermits()
    {
        var options = new RateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1) };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        await limiter.AcquireAsync("client-fixed-3", 2);
        (await limiter.AcquireAsync("client-fixed-3")).Value.IsAcquired.Should().BeFalse();

        // Advance to next window (60s)
        _timeProvider.Advance(TimeSpan.FromSeconds(60));

        var freshResult = await limiter.AcquireAsync("client-fixed-3");
        freshResult.Value.IsAcquired.Should().BeTrue();
        freshResult.Value.RemainingPermits.Should().Be(1);
    }

    [Fact]
    public async Task AcquireAsync_PermitsLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        var limiter = new FixedWindowRateLimiter();

        Func<Task> act = () => limiter.AcquireAsync("client", 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AcquireAsync_NullKey_ThrowsArgumentNullException()
    {
        var limiter = new FixedWindowRateLimiter();

        Func<Task> act = () => limiter.AcquireAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DefaultConstructor_UsesDefaultOptionsAndSystemTimeProvider()
    {
        var limiter = new FixedWindowRateLimiter();

        var result = await limiter.AcquireAsync("default-client", 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(99);
    }

    [Fact]
    public async Task AcquireAsync_DifferentPartitions_AreIsolated()
    {
        var options = new RateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        var resA = await limiter.AcquireAsync("partition-A", 1);
        var resB = await limiter.AcquireAsync("partition-B", 1);

        resA.Value.IsAcquired.Should().BeTrue();
        resB.Value.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_BatchPermits_DecrementsRemainingAccurately()
    {
        var options = new RateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        var first = await limiter.AcquireAsync("batch-client", 4);
        first.Value.IsAcquired.Should().BeTrue();
        first.Value.RemainingPermits.Should().Be(6);

        var second = await limiter.AcquireAsync("batch-client", 6);
        second.Value.IsAcquired.Should().BeTrue();
        second.Value.RemainingPermits.Should().Be(0);

        var third = await limiter.AcquireAsync("batch-client", 1);
        third.Value.IsAcquired.Should().BeFalse();
    }
}

