// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class ConcurrencyRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_WithinPermitLimit_Succeeds()
    {
        var limiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
        {
            PermitLimit = 2
        });

        var result1 = await limiter.AcquireAsync("tenant-1", 1);
        var result2 = await limiter.AcquireAsync("tenant-1", 1);

        Assert.True(result1.IsSuccess);
        Assert.True(result1.Value.IsAcquired);
        Assert.Equal(1, result1.Value.RemainingPermits);

        Assert.True(result2.IsSuccess);
        Assert.True(result2.Value.IsAcquired);
        Assert.Equal(0, result2.Value.RemainingPermits);
    }

    [Fact]
    public async Task AcquireAsync_ExceedingPermitLimit_Rejects()
    {
        var limiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
        {
            PermitLimit = 1
        });

        using var lease1 = (await limiter.AcquireAsync("tenant-1", 1)).Value;
        Assert.True(lease1.IsAcquired);

        var result2 = await limiter.AcquireAsync("tenant-1", 1);
        Assert.True(result2.IsSuccess);
        Assert.False(result2.Value.IsAcquired);
        Assert.NotNull(result2.Value.RetryAfter);
    }

    [Fact]
    public async Task DisposeLease_ReleasesPermit_AllowsSubsequentAcquisition()
    {
        var limiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
        {
            PermitLimit = 1
        });

        var lease1Result = await limiter.AcquireAsync("tenant-1", 1);
        var lease1 = lease1Result.Value;
        Assert.True(lease1.IsAcquired);

        // Second acquisition fails
        var lease2Result = await limiter.AcquireAsync("tenant-1", 1);
        Assert.False(lease2Result.Value.IsAcquired);

        // Dispose first lease
        lease1.Dispose();

        // Third acquisition succeeds now
        var lease3Result = await limiter.AcquireAsync("tenant-1", 1);
        Assert.True(lease3Result.IsSuccess);
        Assert.True(lease3Result.Value.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_DifferentPartitions_AreIsolated()
    {
        var limiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
        {
            PermitLimit = 1
        });

        using var leaseA = (await limiter.AcquireAsync("tenant-A", 1)).Value;
        using var leaseB = (await limiter.AcquireAsync("tenant-B", 1)).Value;

        Assert.True(leaseA.IsAcquired);
        Assert.True(leaseB.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_InvalidParameters_Throws()
    {
        var limiter = new ConcurrencyRateLimiter();

        await Assert.ThrowsAsync<ArgumentNullException>(() => limiter.AcquireAsync(null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => limiter.AcquireAsync("key", 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => limiter.AcquireAsync("key", -5));
    }

    [Fact]
    public void Options_DefaultsAndValidations()
    {
        var options = new ConcurrencyRateLimiterOptions();
        options.PermitLimit.Should().Be(10);

        options.PermitLimit = 50;
        options.PermitLimit.Should().Be(50);

        Action actZero = () => options.PermitLimit = 0;
        Action actNeg = () => options.PermitLimit = -1;

        actZero.Should().Throw<ArgumentOutOfRangeException>();
        actNeg.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task DefaultConstructor_UsesDefaultPermitLimit()
    {
        var limiter = new ConcurrencyRateLimiter();

        var result = await limiter.AcquireAsync("default-client", 1);
        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(9);
    }

    [Fact]
    public void Partition_ReleaseMoreThanCurrent_ClampsToZero()
    {
        var partition = new ConcurrencyPartition(5);

        partition.TryAcquire(2, out var remaining).Should().BeTrue();
        remaining.Should().Be(3);

        // Release 5 when only 2 were acquired -> clamps to 0 active
        partition.Release(5);

        // Now all 5 should be available
        partition.TryAcquire(5, out var fullRemaining).Should().BeTrue();
        fullRemaining.Should().Be(0);
    }

    [Fact]
    public void ConcurrentAcquireAndRelease_MaintainsInvariants()
    {
        const int limit = 5;
        var limiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions { PermitLimit = limit });
        var activeOperations = 0;
        var maxObservedConcurrency = 0;
        var lockObj = new object();

        Parallel.For(0, 50, _ =>
        {
            var task = limiter.AcquireAsync("stress-key", 1);
            task.Wait();
            var lease = task.Result.Value;

            if (lease.IsAcquired)
            {
                var current = Interlocked.Increment(ref activeOperations);
                lock (lockObj)
                {
                    if (current > maxObservedConcurrency)
                    {
                        maxObservedConcurrency = current;
                    }
                }

                // Simulate brief deterministic computation
                for (var i = 0; i < 1000; i++) { }

                Interlocked.Decrement(ref activeOperations);
                lease.Dispose();
            }
        });

        maxObservedConcurrency.Should().BeLessThanOrEqualTo(limit);
    }

    [Fact]
    public void ConcurrencyPartition_TryAcquire_ExceedingLimit_ComputesRemainingPermitsCorrectly()
    {
        var partition = new ConcurrencyPartition(10);

        var first = partition.TryAcquire(7, out var remaining1);
        first.Should().BeTrue();
        remaining1.Should().Be(3);

        // Attempt 5 (7 + 5 = 12 > 10)
        var second = partition.TryAcquire(5, out var remaining2);
        second.Should().BeFalse();
        remaining2.Should().Be(3);
    }

    [Fact]
    public void ConcurrencyPartition_Release_ClampsAtZero()
    {
        var partition = new ConcurrencyPartition(5);
        partition.TryAcquire(2, out _);

        // Over-releasing by 10 should clamp at 0 and not cause negative active permits
        partition.Release(10);

        // Now all 5 should be acquirable
        var success = partition.TryAcquire(5, out var remaining);
        success.Should().BeTrue();
        remaining.Should().Be(0);
    }
}

