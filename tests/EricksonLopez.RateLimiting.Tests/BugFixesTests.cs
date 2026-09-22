// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class BugFixesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public async Task SlidingWindow_AcquireAsync_PermitsLessThanOne_ThrowsArgumentOutOfRangeException(int permits)
    {
        var limiter = new SlidingWindowRateLimiter();

        Func<Task> act = () => limiter.AcquireAsync("key", permits);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public async Task TokenBucket_AcquireAsync_PermitsLessThanOne_ThrowsArgumentOutOfRangeException(int permits)
    {
        var limiter = new TokenBucketRateLimiter();

        Func<Task> act = () => limiter.AcquireAsync("key", permits);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RateLimiterOptions_PermitLimit_Invalid_ThrowsArgumentOutOfRangeException(int limit)
    {
        var options = new RateLimiterOptions();

        Action act = () => options.PermitLimit = limit;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RateLimiterOptions_Window_ZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var options = new RateLimiterOptions();

        Action actZero = () => options.Window = TimeSpan.Zero;
        Action actNegative = () => options.Window = TimeSpan.FromSeconds(-5);

        actZero.Should().Throw<ArgumentOutOfRangeException>();
        actNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RateLimiterOptions_SegmentsPerWindow_Invalid_ThrowsArgumentOutOfRangeException(int segments)
    {
        var options = new RateLimiterOptions();

        Action act = () => options.SegmentsPerWindow = segments;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
