// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

/// <summary>
/// Adversarial regression test suite verifying bounded memory protection,
/// clock skew resistance, reset time computation, and extreme concurrency invariants.
/// </summary>
public sealed class AdversarialRegressionTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task FixedWindow_ClockJumpBackwards_MustNotCorruptCounters()
    {
        var timeProvider = new MutableTimeProvider();
        var options = new RateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromSeconds(10)
        };
        var limiter = new FixedWindowRateLimiter(options, timeProvider);

        // Consume 5 permits
        var r1 = await limiter.AcquireAsync("clock-victim", 5);
        r1.Value.IsAcquired.Should().BeTrue();
        r1.Value.RemainingPermits.Should().Be(0);

        // 6th must be rejected
        var r2 = await limiter.AcquireAsync("clock-victim", 1);
        r2.Value.IsAcquired.Should().BeFalse();

        // Simulate NTP backward clock jump of 1 hour
        timeProvider.UtcNow = timeProvider.UtcNow.AddHours(-1);

        // Attempting to acquire must not throw, nor prematurely grant permits by rewinding index
        var r3 = await limiter.AcquireAsync("clock-victim", 1);
        r3.Value.IsAcquired.Should().BeFalse("backward clock jump must not reset current window usage");
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [Fact]
    public async Task TokenBucket_SuccessfulLease_MustPopulateAccurateResetTime()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(10) // 1 token per second
        };
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        // Acquire 4 tokens -> 6 tokens remaining -> needs 4 seconds to refill to 10
        var now = _timeProvider.GetUtcNow();
        var lease = (await limiter.AcquireAsync("reset-test", 4)).Value;

        lease.IsAcquired.Should().BeTrue();
        lease.RemainingPermits.Should().Be(6);
        lease.ResetTime.Should().NotBeNull();
        lease.ResetTime!.Value.Should().Be(now.AddSeconds(4));
    }

    [Fact]
    public async Task MemoryDefense_ActiveKeyFlooding_MustEnforceMaxPartitionsBound()
    {
        // Vector: Attacker sends 200 distinct keys within the same active window.
        // If MaxPartitions is 50, the limiter must bound memory by rejecting new keys
        // rather than allowing unbounded dictionary growth.
        var options = new RateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10), // Long window so none are idle
            MaxPartitions = 50
        };
        var limiter = new FixedWindowRateLimiter(options, _timeProvider);

        int accepted = 0;
        int rejectedDueToCapacity = 0;

        for (int i = 0; i < 200; i++)
        {
            var lease = (await limiter.AcquireAsync($"flood-key-{i}", 1)).Value;
            if (lease.IsAcquired)
            {
                accepted++;
            }
            else
            {
                rejectedDueToCapacity++;
            }
        }

        // Bounded capacity invariant: exactly MaxPartitions are accepted, excess rejected
        accepted.Should().Be(50);
        rejectedDueToCapacity.Should().Be(150);

        // An existing accepted key must still be able to acquire permits
        var existingKeyLease = (await limiter.AcquireAsync("flood-key-0", 1)).Value;
        existingKeyLease.IsAcquired.Should().BeTrue("existing partition remains functional under partition table saturation");
    }

    [Fact]
    public async Task MemoryDefense_SlidingWindow_ActiveKeyFlooding_MustEnforceMaxPartitionsBound()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10),
            SegmentsPerWindow = 6,
            MaxPartitions = 50
        };
        var limiter = new SlidingWindowRateLimiter(options, _timeProvider);

        int accepted = 0;
        int rejectedDueToCapacity = 0;

        for (int i = 0; i < 200; i++)
        {
            var lease = (await limiter.AcquireAsync($"flood-sw-{i}", 1)).Value;
            if (lease.IsAcquired)
            {
                accepted++;
            }
            else
            {
                rejectedDueToCapacity++;
            }
        }

        accepted.Should().Be(50);
        rejectedDueToCapacity.Should().Be(150);
    }

    [Fact]
    public async Task MemoryDefense_TokenBucket_ActiveKeyFlooding_MustEnforceMaxPartitionsBound()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10),
            MaxPartitions = 50
        };
        var limiter = new TokenBucketRateLimiter(options, _timeProvider);

        int accepted = 0;
        int rejectedDueToCapacity = 0;

        for (int i = 0; i < 200; i++)
        {
            var lease = (await limiter.AcquireAsync($"flood-tb-{i}", 1)).Value;
            if (lease.IsAcquired)
            {
                accepted++;
            }
            else
            {
                rejectedDueToCapacity++;
            }
        }

        accepted.Should().Be(50);
        rejectedDueToCapacity.Should().Be(150);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void Stress_MultiThreaded_FixedWindow_NeverExceedsMathematicalPermitLimit(int threadsCount)
    {
        const int limit = 100;
        var limiter = new FixedWindowRateLimiter(new RateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromHours(1)
        });

        int totalAcquired = 0;
        var threads = new Thread[threadsCount];
        using var startSignal = new ManualResetEventSlim(false);

        for (int i = 0; i < threadsCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                startSignal.Wait();
                for (int j = 0; j < 50; j++)
                {
                    var lease = limiter.AcquireAsync("shared-hot-key", 1).GetAwaiter().GetResult().Value;
                    if (lease.IsAcquired)
                    {
                        Interlocked.Increment(ref totalAcquired);
                    }
                }
            });
            threads[i].Start();
        }

        startSignal.Set();
        for (int i = 0; i < threadsCount; i++)
        {
            threads[i].Join();
        }

        var expected = Math.Min(limit, threadsCount * 50);
        totalAcquired.Should().Be(expected, $"multi-threaded contention across {threadsCount} threads must strictly respect limit {expected}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void Stress_MultiThreaded_TokenBucket_NeverExceedsMathematicalPermitLimit(int threadsCount)
    {
        const int limit = 100;
        var limiter = new TokenBucketRateLimiter(new RateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromHours(1)
        });

        int totalAcquired = 0;
        var threads = new Thread[threadsCount];
        using var startSignal = new ManualResetEventSlim(false);

        for (int i = 0; i < threadsCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                startSignal.Wait();
                for (int j = 0; j < 50; j++)
                {
                    var lease = limiter.AcquireAsync("shared-tb-key", 1).GetAwaiter().GetResult().Value;
                    if (lease.IsAcquired)
                    {
                        Interlocked.Increment(ref totalAcquired);
                    }
                }
            });
            threads[i].Start();
        }

        startSignal.Set();
        for (int i = 0; i < threadsCount; i++)
        {
            threads[i].Join();
        }

        var expectedTb = Math.Min(limit, threadsCount * 50);
        totalAcquired.Should().Be(expectedTb, $"Token bucket across {threadsCount} threads must strictly respect capacity {expectedTb}");
    }
}
