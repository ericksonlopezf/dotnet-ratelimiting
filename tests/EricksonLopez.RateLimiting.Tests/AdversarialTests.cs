// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

/// <summary>
/// Adversarial attack suite attempting to break rate limiting invariants:
/// integer overflow, lease double disposal, unbounded cardinality, and clock anomalies.
/// </summary>
public sealed class AdversarialTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Attack_FixedWindow_IntegerOverflow_MustNotBypassLimit()
    {
        // Vector: Attacker attempts to acquire 1 permit, then int.MaxValue permits.
        // In unchecked arithmetic: 1 + int.MaxValue = int.MinValue (-2,147,483,648).
        // If unchecked, -2,147,483,648 <= permitLimit (e.g. 10) evaluates to TRUE!
        var limiter = new FixedWindowRateLimiter(new RateLimiterOptions { PermitLimit = 10 }, _timeProvider);

        // First request consumes 1 permit
        var first = await limiter.AcquireAsync("victim-key", 1);
        first.Value.IsAcquired.Should().BeTrue();

        // Second request asks for int.MaxValue permits. This MUST be rejected or throw.
        // It must NEVER be granted!
        var attackResult = await limiter.AcquireAsync("victim-key", int.MaxValue);

        // In a secure implementation, the attack must NOT be acquired!
        attackResult.Value.IsAcquired.Should().BeFalse(
            "Integer overflow (1 + int.MaxValue) must not wrap around to grant unlimited permits!");
    }

    [Fact]
    public async Task Attack_SlidingWindow_IntegerOverflow_MustNotBypassLimit()
    {
        var limiter = new SlidingWindowRateLimiter(new RateLimiterOptions { PermitLimit = 10 }, _timeProvider);

        var first = await limiter.AcquireAsync("victim-key", 1);
        first.Value.IsAcquired.Should().BeTrue();

        var attackResult = await limiter.AcquireAsync("victim-key", int.MaxValue);

        attackResult.Value.IsAcquired.Should().BeFalse(
            "Integer overflow in sliding window must not wrap around to grant unlimited permits!");
    }

    [Fact]
    public async Task Attack_Concurrency_IntegerOverflow_MustNotBypassLimit()
    {
        var limiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions { PermitLimit = 10 });

        var first = await limiter.AcquireAsync("victim-key", 1);
        first.Value.IsAcquired.Should().BeTrue();

        var attackResult = await limiter.AcquireAsync("victim-key", int.MaxValue);

        attackResult.Value.IsAcquired.Should().BeFalse(
            "Integer overflow in concurrency limiter must not wrap around to bypass concurrency limit!");
    }

    [Fact]
    public async Task Attack_TokenBucket_IntegerOverflow_MustNotThrowOverflowException()
    {
        var limiter = new TokenBucketRateLimiter(new RateLimiterOptions { PermitLimit = 10 }, _timeProvider);

        // Requesting int.MaxValue permits in TokenBucket should be safely rejected without unhandled OverflowException
        var attackResult = await limiter.AcquireAsync("victim-key", int.MaxValue);

        attackResult.Value.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task Attack_Concurrency_DoubleLeaseDisposal_MustNotFreeUnallocatedSlots()
    {
        // Vector: Client A acquires a permit. Client B acquires a permit.
        // PermitLimit is 2 (fully saturated). Client C is rejected.
        // Client A calls lease.Dispose() TWICE.
        // If not idempotent, active count drops from 1 to 0, prematurely releasing Client B's slot!
        // Client C and Client D can then both acquire permits, yielding 3 concurrent operations when limit was 2!
        var limiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions { PermitLimit = 2 });

        var leaseA = (await limiter.AcquireAsync("tenant-race", 1)).Value;
        var leaseB = (await limiter.AcquireAsync("tenant-race", 1)).Value;

        leaseA.IsAcquired.Should().BeTrue();
        leaseB.IsAcquired.Should().BeTrue();

        // Verify saturated: third acquire fails
        var leaseC1 = (await limiter.AcquireAsync("tenant-race", 1)).Value;
        leaseC1.IsAcquired.Should().BeFalse();

        // Client A disposes once
        leaseA.Dispose();

        // Client A maliciously or accidentally disposes AGAIN
        leaseA.Dispose();
        leaseA.Dispose();

        // At this point, Client B is still running! Active permits should be 1.
        // Only 1 slot should be free!
        var leaseC2 = (await limiter.AcquireAsync("tenant-race", 1)).Value;
        leaseC2.IsAcquired.Should().BeTrue("one slot was freed by leaseA's first dispose");

        // Now total active should be 2 (Client B + Client C2). Attempting another acquire MUST FAIL!
        var leaseD = (await limiter.AcquireAsync("tenant-race", 1)).Value;
        leaseD.IsAcquired.Should().BeFalse(
            "Repeated disposal of leaseA must NOT release permits belonging to leaseB!");
    }

    [Fact]
    public async Task Attack_TokenBucket_ClockJumpBackwards_MustNotCorruptTokens()
    {
        // Vector: System clock jumps backwards (e.g. NTP correction or VM migration).
        var timeProvider = new MutableTimeProvider();
        var limiter = new TokenBucketRateLimiter(
            new RateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromSeconds(10) },
            timeProvider);

        // Initial acquisition consumes 5 permits -> bucket has 0 remaining
        var r1 = await limiter.AcquireAsync("clock-key", 5);
        r1.Value.IsAcquired.Should().BeTrue();

        // Advance 5 seconds -> refills 2.5 tokens
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(5);

        // Now jump backwards 10 seconds!
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(-10);

        // Under clock jump backwards, tokens should not become negative or nan
        var r2 = await limiter.AcquireAsync("clock-key", 1);
        // Either rejected or handled gracefully, but no exception or corruption
        r2.IsSuccess.Should().BeTrue();
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
