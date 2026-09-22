// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.RateLimiting;
using BenchmarkDotNet.Attributes;

namespace EricksonLopez.RateLimiting.Benchmarks;

[MemoryDiagnoser]
public class RateLimiterHotPathBenchmarks : IDisposable
{
    private TokenBucketRateLimiter _ericksonLopezTokenBucket = null!;
    private System.Threading.RateLimiting.TokenBucketRateLimiter _bclTokenBucket = null!;
    private SlidingWindowRateLimiter _ericksonLopezSlidingWindow = null!;
    private FixedWindowRateLimiter _ericksonLopezFixedWindow = null!;
    private bool _disposed;

    [GlobalSetup]
    public void Setup()
    {
        var options = new RateLimiterOptions
        {
            PermitLimit = 10_000_000,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 6
        };

        _ericksonLopezTokenBucket = new TokenBucketRateLimiter(options);
        _ericksonLopezSlidingWindow = new SlidingWindowRateLimiter(options);
        _ericksonLopezFixedWindow = new FixedWindowRateLimiter(options);

        _bclTokenBucket = new System.Threading.RateLimiting.TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10_000_000,
            ReplenishmentPeriod = TimeSpan.FromHours(1),
            TokensPerPeriod = 10_000_000,
            AutoReplenishment = false
        });

        // Warmup partitions
        _ = _ericksonLopezTokenBucket.AcquireAsync("benchmark-key", 1);
        _ = _ericksonLopezSlidingWindow.AcquireAsync("benchmark-key", 1);
        _ = _ericksonLopezFixedWindow.AcquireAsync("benchmark-key", 1);
        _ = _bclTokenBucket.AttemptAcquire(1);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _bclTokenBucket?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true)]
    public bool BCL_TokenBucket_AttemptAcquire()
    {
        using var lease = _bclTokenBucket.AttemptAcquire(1);
        return lease.IsAcquired;
    }

    [Benchmark]
    public bool EricksonLopez_TokenBucket_AcquireAsync()
    {
        var task = _ericksonLopezTokenBucket.AcquireAsync("benchmark-key", 1);
        var result = task.Result;
        return result.Value.IsAcquired;
    }

    [Benchmark]
    public bool EricksonLopez_SlidingWindow_AcquireAsync()
    {
        var task = _ericksonLopezSlidingWindow.AcquireAsync("benchmark-key", 1);
        var result = task.Result;
        return result.Value.IsAcquired;
    }

    [Benchmark]
    public bool EricksonLopez_FixedWindow_AcquireAsync()
    {
        var task = _ericksonLopezFixedWindow.AcquireAsync("benchmark-key", 1);
        var result = task.Result;
        return result.Value.IsAcquired;
    }

    [Benchmark]
    public bool EricksonLopez_RateLimitLease_Struct_ZeroAllocation()
    {
        var lease = RateLimitLease.Successful(100);
        return lease.IsAcquired;
    }
}
