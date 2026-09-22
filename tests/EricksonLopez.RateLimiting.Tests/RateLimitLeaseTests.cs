// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class RateLimitLeaseTests
{
    [Fact]
    public void Successful_WithOnlyRemainingPermits_InitializesCorrectly()
    {
        var lease = RateLimitLease.Successful(remainingPermits: 10);

        lease.IsAcquired.Should().BeTrue();
        lease.RemainingPermits.Should().Be(10);
        lease.RetryAfter.Should().BeNull();
        lease.ResetTime.Should().BeNull();
        lease.DisposeAction.Should().BeNull();
    }

    [Fact]
    public void Successful_WithResetTimeAndDisposeAction_InitializesAllProperties()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(1);
        var disposed = false;
        Action disposeAction = () => disposed = true;

        var lease = RateLimitLease.Successful(
            remainingPermits: 5,
            resetTime: reset,
            disposeAction: disposeAction);

        lease.IsAcquired.Should().BeTrue();
        lease.RemainingPermits.Should().Be(5);
        lease.RetryAfter.Should().BeNull();
        lease.ResetTime.Should().Be(reset);
        lease.DisposeAction.Should().BeSameAs(disposeAction);

        lease.Dispose();
        disposed.Should().BeTrue();
    }

    [Fact]
    public void Rejected_WithRetryAfterAndResetTime_InitializesCorrectly()
    {
        var retryAfter = TimeSpan.FromSeconds(30);
        var reset = DateTimeOffset.UtcNow.AddSeconds(30);

        var lease = RateLimitLease.Rejected(retryAfter, reset);

        lease.IsAcquired.Should().BeFalse();
        lease.RemainingPermits.Should().Be(0);
        lease.RetryAfter.Should().Be(retryAfter);
        lease.ResetTime.Should().Be(reset);
        lease.DisposeAction.Should().BeNull();
    }

    [Fact]
    public void Rejected_WithoutResetTime_InitializesWithNullResetTime()
    {
        var retryAfter = TimeSpan.FromSeconds(15);

        var lease = RateLimitLease.Rejected(retryAfter);

        lease.IsAcquired.Should().BeFalse();
        lease.RemainingPermits.Should().Be(0);
        lease.RetryAfter.Should().Be(retryAfter);
        lease.ResetTime.Should().BeNull();
        lease.DisposeAction.Should().BeNull();
    }

    [Fact]
    public void Dispose_WhenDisposeActionIsNull_DoesNotThrow()
    {
        var lease = RateLimitLease.Successful(remainingPermits: 1);

        var act = () => lease.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void PrimaryConstructor_DefaultOptionalParameters_InitializesDefaults()
    {
        var lease = new RateLimitLease(IsAcquired: true, RemainingPermits: 7);

        lease.IsAcquired.Should().BeTrue();
        lease.RemainingPermits.Should().Be(7);
        lease.RetryAfter.Should().BeNull();
        lease.ResetTime.Should().BeNull();
        lease.DisposeAction.Should().BeNull();
    }

    [Fact]
    public void RecordStruct_ValueEquality_BehavesExpectedly()
    {
        var reset = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var retry = TimeSpan.FromSeconds(10);

        var lease1 = new RateLimitLease(true, 5, retry, reset, null);
        var lease2 = new RateLimitLease(true, 5, retry, reset, null);
        var lease3 = new RateLimitLease(false, 0, retry, reset, null);

        (lease1 == lease2).Should().BeTrue();
        (lease1 != lease3).Should().BeTrue();
        lease1.Equals(lease2).Should().BeTrue();
        lease1.GetHashCode().Should().Be(lease2.GetHashCode());
    }

    [Fact]
    public void Deconstruct_FourParameters_CompatibleWithV1_0_0()
    {
        var reset = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var retry = TimeSpan.FromSeconds(15);
        var lease = new RateLimitLease(true, 42, retry, reset);

        var (isAcquired, remaining, retryAfter, resetTime) = lease;

        isAcquired.Should().BeTrue();
        remaining.Should().Be(42);
        retryAfter.Should().Be(retry);
        resetTime.Should().Be(reset);
    }

    [Fact]
    public void Deconstruct_FiveParameters_V1_1_0_IncludesDisposeAction()
    {
        var disposed = false;
        Action disposeAction = () => disposed = true;
        var lease = RateLimitLease.Successful(10, null, disposeAction);

        var (isAcquired, remaining, retryAfter, resetTime, action) = lease;

        isAcquired.Should().BeTrue();
        remaining.Should().Be(10);
        retryAfter.Should().BeNull();
        resetTime.Should().BeNull();
        action.Should().BeSameAs(disposeAction);

        action!();
        disposed.Should().BeTrue();
    }

    [Fact]
    public void Constructor_FourParameters_CompatibleWithV1_0_0()
    {
        var reset = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var retry = TimeSpan.FromSeconds(5);
        var lease = new RateLimitLease(true, 8, retry, reset);

        lease.IsAcquired.Should().BeTrue();
        lease.RemainingPermits.Should().Be(8);
        lease.RetryAfter.Should().Be(retry);
        lease.ResetTime.Should().Be(reset);
        lease.DisposeAction.Should().BeNull();
    }
}
