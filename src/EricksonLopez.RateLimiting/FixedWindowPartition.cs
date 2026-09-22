// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting;

internal sealed class FixedWindowPartition
{
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private int _count;
    private long _lastWindowIndex;
    private readonly object _lock = new();

    public FixedWindowPartition(int permitLimit, TimeSpan window, DateTimeOffset startTime)
    {
        _permitLimit = permitLimit;
        _window = window;
        _lastWindowIndex = startTime.UtcTicks / window.Ticks;
    }

    public RateLimitLease TryAcquire(int permits, DateTimeOffset now)
    {
        lock (_lock)
        {
            var currentWindowIndex = Math.Max(_lastWindowIndex, now.UtcTicks / _window.Ticks);
            if (currentWindowIndex > _lastWindowIndex)
            {
                _count = 0;
                _lastWindowIndex = currentWindowIndex;
            }

            var nextWindowTicks = (_lastWindowIndex + 1) * _window.Ticks;
            var windowEnd = new DateTimeOffset(nextWindowTicks, TimeSpan.Zero);

            if (permits <= _permitLimit && _count <= _permitLimit - permits)
            {
                _count += permits;
                var remaining = _permitLimit - _count;
                return RateLimitLease.Successful(remaining, windowEnd, null, _permitLimit);
            }

            var retryAfterTicks = Math.Max(1, nextWindowTicks - now.UtcTicks);
            var retryAfter = TimeSpan.FromTicks(retryAfterTicks);
            return RateLimitLease.Rejected(retryAfter, windowEnd, _permitLimit);
        }
    }

    public bool IsIdle(DateTimeOffset now)
    {
        lock (_lock)
        {
            return (now.UtcTicks / _window.Ticks) > (_lastWindowIndex + 1);
        }
    }
}
