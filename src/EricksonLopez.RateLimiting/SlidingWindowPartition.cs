// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting;

internal sealed class SlidingWindowPartition
{
    private readonly int[] _segments;
    private readonly TimeSpan _segmentInterval;
    private readonly int _permitLimit;
    private long _lastSegmentIndex;
    private readonly object _lock = new();

    public SlidingWindowPartition(int permitLimit, TimeSpan window, int segmentsPerWindow, DateTimeOffset startTime)
    {
        _permitLimit = permitLimit;
        _segments = new int[Math.Max(1, segmentsPerWindow)];
        var intervalTicks = Math.Max(1, window.Ticks / _segments.Length);
        _segmentInterval = TimeSpan.FromTicks(intervalTicks);
        _lastSegmentIndex = Math.Max(0, startTime.UtcTicks / _segmentInterval.Ticks);
    }

    public RateLimitLease TryAcquire(int permits, DateTimeOffset now)
    {
        lock (_lock)
        {
            var rawSegmentIndex = now.UtcTicks / _segmentInterval.Ticks;
            // Enforce monotonic time: never allow clock skew or backward jumps to move segment index backward
            var currentSegmentIndex = Math.Max(_lastSegmentIndex, rawSegmentIndex);
            var segmentsToAdvance = currentSegmentIndex - _lastSegmentIndex;

            if (segmentsToAdvance > 0)
            {
                var clearCount = (int)Math.Min(segmentsToAdvance, _segments.Length);
                for (var i = 1; i <= clearCount; i++)
                {
                    var indexToClear = (int)(((_lastSegmentIndex + i) % _segments.Length + _segments.Length) % _segments.Length);
                    _segments[indexToClear] = 0;
                }
                _lastSegmentIndex = currentSegmentIndex;
            }

            var currentUsage = 0;
            for (var i = 0; i < _segments.Length; i++)
            {
                currentUsage += _segments[i];
            }

            var windowEndTicks = (_lastSegmentIndex + 1) * _segmentInterval.Ticks;
            var windowEnd = new DateTimeOffset(Math.Max(now.UtcTicks, windowEndTicks), TimeSpan.Zero);

            if (permits <= _permitLimit && currentUsage <= _permitLimit - permits)
            {
                var activeIndex = (int)(((currentSegmentIndex % _segments.Length) + _segments.Length) % _segments.Length);
                _segments[activeIndex] += permits;
                var remaining = _permitLimit - (currentUsage + permits);
                return RateLimitLease.Successful(remaining, windowEnd, null, _permitLimit);
            }

            // Compute exact Retry-After by finding when enough permits roll out of the sliding window
            long earliestSufficientExpiryTicks = (_lastSegmentIndex + _segments.Length) * _segmentInterval.Ticks;

            if (permits <= _permitLimit)
            {
                var neededPermits = permits - (_permitLimit - currentUsage);
                var freed = 0;
                for (var offset = 0; offset < _segments.Length; offset++)
                {
                    var segIdx = currentSegmentIndex - _segments.Length + 1 + offset;
                    var arrayIdx = (int)(((segIdx % _segments.Length) + _segments.Length) % _segments.Length);
                    freed += _segments[arrayIdx];
                    if (freed >= neededPermits)
                    {
                        earliestSufficientExpiryTicks = (segIdx + _segments.Length) * _segmentInterval.Ticks;
                        break;
                    }
                }
            }

            var retryAfterTicks = Math.Max(1, earliestSufficientExpiryTicks - now.UtcTicks);

            var retryAfter = TimeSpan.FromTicks(retryAfterTicks);
            var resetTime = new DateTimeOffset(earliestSufficientExpiryTicks, TimeSpan.Zero);
            return RateLimitLease.Rejected(retryAfter, resetTime, _permitLimit);
        }
    }

    public bool IsIdle(DateTimeOffset now)
    {
        lock (_lock)
        {
            var currentSegmentIndex = Math.Max(0, now.UtcTicks / _segmentInterval.Ticks);
            return (currentSegmentIndex - _lastSegmentIndex) > _segments.Length;
        }
    }
}
