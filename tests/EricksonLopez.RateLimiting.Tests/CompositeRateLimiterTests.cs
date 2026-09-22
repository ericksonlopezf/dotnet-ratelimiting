// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using NSubstitute;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class CompositeRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_AllChildrenSucceed_ReturnsSuccessfulCompositeLease()
    {
        var limiter1 = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(1)
        });

        var limiter2 = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1)
        });

        var composite = new CompositeRateLimiter(limiter1, limiter2);

        var result = await composite.AcquireAsync("tenant-1", 1);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAcquired);
        Assert.Equal(9, result.Value.RemainingPermits); // min(9, 99)
    }

    [Fact]
    public async Task AcquireAsync_FirstChildRejects_ShortCircuitsAndRejects()
    {
        var limiter1 = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1)
        });

        var limiter2 = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1)
        });

        var composite = new CompositeRateLimiter(limiter1, limiter2);

        var res1 = await composite.AcquireAsync("tenant-1", 1);
        Assert.True(res1.Value.IsAcquired);

        var res2 = await composite.AcquireAsync("tenant-1", 1);
        Assert.False(res2.Value.IsAcquired);
        Assert.NotNull(res2.Value.RetryAfter);
    }

    [Fact]
    public async Task AcquireAsync_SecondChildRejects_RollsBackFirstChildLease()
    {
        var concurrencyLimiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
        {
            PermitLimit = 5
        });

        var fixedLimiter = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1)
        });

        var composite = new CompositeRateLimiter(concurrencyLimiter, fixedLimiter);

        // First call consumes the 1 permit on fixedLimiter and 1 on concurrencyLimiter
        var res1 = await composite.AcquireAsync("tenant-1", 1);
        Assert.True(res1.Value.IsAcquired);

        // Second call: concurrency succeeds (has 4 left), but fixedLimiter rejects!
        var res2 = await composite.AcquireAsync("tenant-1", 1);
        Assert.False(res2.Value.IsAcquired);

        // Verify concurrency limiter did NOT leak the permit from second attempt
        // If res1 is disposed, concurrency should have 5 available again
        res1.Value.Dispose();

        var probe = await concurrencyLimiter.AcquireAsync("tenant-1", 5);
        Assert.True(probe.Value.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_InvalidParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeRateLimiter((IRateLimiter[])null!));
        var ex = Assert.Throws<ArgumentException>(() => new CompositeRateLimiter(Array.Empty<IRateLimiter>()));
        AwesomeAssertions.AssertionExtensions.Should(ex.Message).Contain("At least one child rate limiter must be specified.");
        AwesomeAssertions.AssertionExtensions.Should(ex.ParamName).Be("limiters");

        var dummy = new FixedWindowRateLimiter();
        var composite = new CompositeRateLimiter(dummy);

        await Assert.ThrowsAsync<ArgumentNullException>(() => composite.AcquireAsync(null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => composite.AcquireAsync("key", 0));
    }

    [Fact]
    public void Limiters_Property_ReturnsAllChildLimiters()
    {
        var l1 = new FixedWindowRateLimiter();
        var l2 = new SlidingWindowRateLimiter();
        var composite = new CompositeRateLimiter(l1, l2);

        Assert.Equal(2, composite.Limiters.Count);
        Assert.Same(l1, composite.Limiters[0]);
        Assert.Same(l2, composite.Limiters[1]);
    }

    [Fact]
    public async Task AcquireAsync_ChildFails_RollsBackAcquiredLeasesAndReturnsFailure()
    {
        var concurrencyLimiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions { PermitLimit = 5 });
        var mockFailingLimiter = NSubstitute.Substitute.For<IRateLimiter>();
        mockFailingLimiter.AcquireAsync(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<int>(), NSubstitute.Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(EricksonLopez.Result.Result<RateLimitLease>.Failure(EricksonLopez.Result.Error.Failure("Test.Fail", "Error in child"))));

        var composite = new CompositeRateLimiter(concurrencyLimiter, mockFailingLimiter);

        var result = await composite.AcquireAsync("tenant-fail", 1);

        Assert.True(result.IsFailure);
        Assert.Equal("Test.Fail", result.Error.Code);

        // Concurrency limiter should have rolled back its acquired lease, so all 5 permits must be free
        var probe = await concurrencyLimiter.AcquireAsync("tenant-fail", 5);
        Assert.True(probe.Value.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_ChildRejectsWithNullRetryAfter_UsesFallbackOneSecond()
    {
        var mockLimiter = NSubstitute.Substitute.For<IRateLimiter>();
        // Return a rejected lease with null RetryAfter
        var rejectedWithoutRetryAfter = new RateLimitLease(IsAcquired: false, RemainingPermits: 0, RetryAfter: null, ResetTime: null);
        mockLimiter.AcquireAsync(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<int>(), NSubstitute.Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(EricksonLopez.Result.Result<RateLimitLease>.Success(rejectedWithoutRetryAfter)));

        var composite = new CompositeRateLimiter(mockLimiter);
        var result = await composite.AcquireAsync("tenant-fallback", 1);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsAcquired);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Value.RetryAfter);
    }

    [Fact]
    public async Task AcquireAsync_ChildRejectsWithExplicitRetryAfter_PreservesChildRetryAfter()
    {
        var mockLimiter = NSubstitute.Substitute.For<IRateLimiter>();
        var rejectedWithRetryAfter = new RateLimitLease(IsAcquired: false, RemainingPermits: 0, RetryAfter: TimeSpan.FromSeconds(42), ResetTime: null);
        mockLimiter.AcquireAsync(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<int>(), NSubstitute.Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(EricksonLopez.Result.Result<RateLimitLease>.Success(rejectedWithRetryAfter)));

        var composite = new CompositeRateLimiter(mockLimiter);
        var result = await composite.AcquireAsync("tenant-custom-retry", 1);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsAcquired);
        Assert.Equal(TimeSpan.FromSeconds(42), result.Value.RetryAfter);
    }

    [Fact]
    public async Task AcquireAsync_ChildrenWithoutResetTime_ReturnsSuccessfulLeaseWithNullResetTime()
    {
        var mockL1 = NSubstitute.Substitute.For<IRateLimiter>();
        mockL1.AcquireAsync(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<int>(), NSubstitute.Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(EricksonLopez.Result.Result<RateLimitLease>.Success(RateLimitLease.Successful(10, null))));

        var mockL2 = NSubstitute.Substitute.For<IRateLimiter>();
        mockL2.AcquireAsync(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<int>(), NSubstitute.Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(EricksonLopez.Result.Result<RateLimitLease>.Success(RateLimitLease.Successful(20, null))));

        var composite = new CompositeRateLimiter(mockL1, mockL2);
        var result = await composite.AcquireAsync("tenant-no-reset", 1);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAcquired);
        Assert.Null(result.Value.ResetTime);
    }

    [Fact]
    public async Task AcquireAsync_SuccessfulLeaseWithMultipleChildren_CombinesDisposalAndTakesMaxResetTime()
    {
        var reset1 = DateTimeOffset.UtcNow.AddSeconds(10);
        var reset2 = DateTimeOffset.UtcNow.AddSeconds(30);

        var disposed1 = false;
        var disposed2 = false;

        var mockL1 = NSubstitute.Substitute.For<IRateLimiter>();
        mockL1.AcquireAsync(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<int>(), NSubstitute.Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(EricksonLopez.Result.Result<RateLimitLease>.Success(RateLimitLease.Successful(10, reset1, () => disposed1 = true))));

        var mockL2 = NSubstitute.Substitute.For<IRateLimiter>();
        mockL2.AcquireAsync(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<int>(), NSubstitute.Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(EricksonLopez.Result.Result<RateLimitLease>.Success(RateLimitLease.Successful(20, reset2, () => disposed2 = true))));

        var composite = new CompositeRateLimiter(mockL1, mockL2);
        var result = await composite.AcquireAsync("tenant-multi", 1);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAcquired);
        Assert.Equal(10, result.Value.RemainingPermits); // min(10, 20)
        Assert.Equal(reset2, result.Value.ResetTime);    // max(reset1, reset2)

        // Disposing composite lease should invoke both child dispose actions
        result.Value.Dispose();
        Assert.True(disposed1);
        Assert.True(disposed2);
    }
}

