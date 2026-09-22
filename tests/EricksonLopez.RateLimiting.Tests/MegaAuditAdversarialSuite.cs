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
/// Adversarial vulnerability suite detecting concurrency races, memory leaks,
/// cancellation permit starvation, and mathematical boundary flaws.
/// </summary>
public sealed class MegaAuditAdversarialSuite
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void TokenBucket_DrainedBucket_MustBecomeIdle_AfterRefillTimeElapsed()
    {
        // Vulnerability: TokenBucketPartition.IsIdle only checked _currentTokens >= _capacity,
        // but _currentTokens was never updated inside IsIdle()!
        // A partition drained to 0 tokens that is never requested again stays at _currentTokens = 0 forever,
        // so IsIdle() returned false forever, preventing pruning and causing an unbounded memory leak.
        var partition = new TokenBucketPartition(10, TimeSpan.FromSeconds(10), _timeProvider.GetUtcNow());

        // Drain the bucket completely
        var lease = partition.TryAcquire(10, _timeProvider.GetUtcNow());
        lease.IsAcquired.Should().BeTrue();

        // Advance time by 30 seconds (3x the window, plenty of time to refill to capacity)
        var future = _timeProvider.GetUtcNow().AddSeconds(30);

        // Under the flawed code, partition.IsIdle(future) returned FALSE!
        // It must return TRUE because 30 seconds elapsed, refilling the bucket.
        var isIdle = partition.IsIdle(future);
        isIdle.Should().BeTrue("a drained bucket that had enough elapsed time to refill must be recognized as idle");
    }

    [Fact]
    public async Task CompositeLimiter_CancellationDuringChain_MustNotLeakConcurrencyPermits()
    {
        // Vulnerability: CompositeRateLimiter evaluated child limiters in a loop.
        // If Limiter 1 (ConcurrencyRateLimiter) succeeded, and cancellation was requested before Limiter 2,
        // the concurrency lease in acquiredLeases was never disposed, permanently leaking the permit.
        var concurrencyLimiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions { PermitLimit = 1 });
        var slowLimiter = new HangingRateLimiter();
        var composite = new CompositeRateLimiter(concurrencyLimiter, slowLimiter);

        using var cts = new CancellationTokenSource();

        // Start acquire and cancel while slowLimiter is hanging
        var acquireTask = composite.AcquireAsync("tenant-leak-test", 1, cts.Token);
        cts.Cancel();

        Func<Task> act = async () => await acquireTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // If the permit was leaked, subsequent acquires on concurrencyLimiter will be REJECTED because active count = 1!
        var verifyLease = await concurrencyLimiter.AcquireAsync("tenant-leak-test", 1);
        verifyLease.Value.IsAcquired.Should().BeTrue(
            "the concurrency permit acquired prior to cancellation must be released upon cancellation");
        verifyLease.Value.Dispose();
    }

    [Fact]
    public void SlidingWindow_ClockJumpBackwards_MustNotThrowIndexOutOfRangeException()
    {
        // Vulnerability: In SlidingWindowPartition, if now is earlier than startTime (NTP jump backwards),
        // currentSegmentIndex can be negative, leading to negative modulo in C# and IndexOutOfRangeException.
        var partition = new SlidingWindowPartition(10, TimeSpan.FromMinutes(1), 6, _timeProvider.GetUtcNow());

        // Clock jumps backwards by 10 minutes
        var backwardsTime = _timeProvider.GetUtcNow().AddMinutes(-10);

        Action act = () => partition.TryAcquire(1, backwardsTime);
        act.Should().NotThrow<IndexOutOfRangeException>(
            "clock jump backwards must be handled gracefully without unhandled index out of range exception");
    }

    [Fact]
    public void SlidingWindow_BurstInRecentSegment_RetryAfterMustReflectWhenOldestNonEmptySegmentExpires()
    {
        // Vulnerability: In SlidingWindowPartition, retryAfter was calculated as:
        // _segmentInterval - (now % _segmentInterval), which assumes permits expire at the end of the CURRENT segment.
        // If segments 0..4 had 0 requests, and segment 5 had 10 requests (filling the limit),
        // at the end of segment 5, segment 0 rolls out (freeing 0 permits!).
        // So the client retrying at retryAfter is immediately rejected again!
        var now = _timeProvider.GetUtcNow();
        // 6 segments, 10 seconds per segment, limit = 10
        var partition = new SlidingWindowPartition(10, TimeSpan.FromSeconds(60), 6, now);

        // Advance to segment 5 (t = 50s) with no prior requests
        var t50 = now.AddSeconds(50);
        var lease = partition.TryAcquire(10, t50);
        lease.IsAcquired.Should().BeTrue();

        // At t = 52s, request 1 more permit (rejected)
        var t52 = now.AddSeconds(52);
        var rejLease = partition.TryAcquire(1, t52);
        rejLease.IsAcquired.Should().BeFalse();

        // The permits won't expire at t = 60s (segment 0 expiring has 0 permits!).
        // They expire when segment 5 rolls out, which is at t = 110s (58 seconds from t=52s)!
        // RetryAfter must be >= 50 seconds, NOT 8 seconds!
        rejLease.RetryAfter.Should().NotBeNull();
        rejLease.RetryAfter!.Value.TotalSeconds.Should().BeGreaterThan(30.0,
            "RetryAfter must point to when tokens actually expire, not just the next segment boundary with 0 expiring tokens");
    }

    [Fact]
    public void ConcurrencyRateLimiter_ConcurrentAcquireAndPrune_MustNotAllowLimitBypass()
    {
        // Vulnerability: PruneIdlePartitions checks IsIdle() and calls TryRemove(key, out _) without synchronization.
        // Under high concurrency with rapid idle transitions, an active partition can be pruned and replaced
        // with a fresh 0-active partition, allowing concurrent permits to exceed PermitLimit.
        const int limit = 5;
        var limiter = new ConcurrencyRateLimiter(new ConcurrencyRateLimiterOptions
        {
            PermitLimit = limit,
            MaxPartitions = 1 // Force pruning on every call
        });

        int maxSimultaneous = 0;
        int currentSimultaneous = 0;
        int violations = 0;

        const int threadCount = 16;
        var threads = new Thread[threadCount];
        using var startSignal = new ManualResetEventSlim(false);

        for (int i = 0; i < threadCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                startSignal.Wait();
                for (int j = 0; j < 50; j++)
                {
                    var res = limiter.AcquireAsync("shared-hot-key", 1).GetAwaiter().GetResult();
                    if (res.Value.IsAcquired)
                    {
                        var count = Interlocked.Increment(ref currentSimultaneous);
                        if (count > limit)
                        {
                            Interlocked.Increment(ref violations);
                        }

                        int initial;
                        do
                        {
                            initial = Volatile.Read(ref maxSimultaneous);
                            if (count <= initial) break;
                        } while (Interlocked.CompareExchange(ref maxSimultaneous, count, initial) != initial);

                        Thread.Sleep(1);
                        Interlocked.Decrement(ref currentSimultaneous);
                        res.Value.Dispose();
                    }
                }
            });
            threads[i].Start();
        }

        startSignal.Set();
        for (int i = 0; i < threadCount; i++)
        {
            threads[i].Join();
        }

        violations.Should().Be(0, $"observed {maxSimultaneous} simultaneous active operations when limit was {limit}");
    }

    private sealed class HangingRateLimiter : IRateLimiter
    {
        public async Task<EricksonLopez.Result.Result<RateLimitLease>> AcquireAsync(
            string key,
            int permits = 1,
            CancellationToken cancellationToken = default)
        {
            // Hang until cancellation
            var tcs = new TaskCompletionSource<EricksonLopez.Result.Result<RateLimitLease>>();
            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task;
        }
    }
}
