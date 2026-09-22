// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Encapsulates thread-safe partition state managing lock-free atomic concurrency slot allocations.
/// </summary>
internal sealed class ConcurrencyPartition
{
    private const int RetiredState = -1;
    private int _activePermits;
    private readonly int _limit;

    public ConcurrencyPartition(int limit)
    {
        _limit = limit;
    }

    public int Limit => _limit;

    public bool IsRetired => Volatile.Read(ref _activePermits) == RetiredState;

    public bool TryRetire()
    {
        return Interlocked.CompareExchange(ref _activePermits, RetiredState, 0) == 0;
    }

    public bool TryAcquire(int permits, out int remainingPermits)
    {
        return TryAcquireEx(permits, out remainingPermits) == ConcurrencyAcquireResult.Acquired;
    }

    public ConcurrencyAcquireResult TryAcquireEx(int permits, out int remainingPermits)
    {
        while (true)
        {
            var current = Volatile.Read(ref _activePermits);
            if (current == RetiredState)
            {
                remainingPermits = 0;
                return ConcurrencyAcquireResult.Retired;
            }

            if (permits > _limit || current > _limit - permits)
            {
                remainingPermits = Math.Max(0, _limit - current);
                return ConcurrencyAcquireResult.Rejected;
            }

            if (Interlocked.CompareExchange(ref _activePermits, current + permits, current) == current)
            {
                remainingPermits = _limit - (current + permits);
                return ConcurrencyAcquireResult.Acquired;
            }
        }
    }

    public void Release(int permits)
    {
        while (true)
        {
            var current = Volatile.Read(ref _activePermits);
            if (current == RetiredState)
            {
                break;
            }

            var next = Math.Max(0, current - permits);
            if (Interlocked.CompareExchange(ref _activePermits, next, current) == current)
            {
                break;
            }
        }
    }

    public bool IsIdle() => Volatile.Read(ref _activePermits) == 0;
}
