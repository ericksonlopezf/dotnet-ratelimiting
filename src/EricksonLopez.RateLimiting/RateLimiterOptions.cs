// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Specifies configuration options for time-windowed rate limiters.
/// </summary>
public sealed class RateLimiterOptions
{
    private int _permitLimit = 100;
    private TimeSpan _window = TimeSpan.FromMinutes(1);
    private int _segmentsPerWindow = 6;
    private int _maxPartitions = 10_000;

    /// <summary>
    /// Gets or sets the maximum number of permitted requests allowed within each time window.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1</exception>
    public int PermitLimit
    {
        get => _permitLimit;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _permitLimit = value;
        }
    }

    /// <summary>
    /// Gets or sets the duration of the rate limiting time window.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than or equal to <see cref="TimeSpan.Zero"/></exception>
    public TimeSpan Window
    {
        get => _window;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Window must be greater than zero.");
            }
            _window = value;
        }
    }

    /// <summary>
    /// Gets or sets the number of discrete segments into which each sliding window is divided.
    /// </summary>
    /// <remarks>
    /// This property is used exclusively by <see cref="SlidingWindowRateLimiter"/> to smooth burst
    /// traffic across segment boundaries. It is ignored by <see cref="FixedWindowRateLimiter"/>
    /// and <see cref="TokenBucketRateLimiter"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1</exception>
    public int SegmentsPerWindow
    {
        get => _segmentsPerWindow;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _segmentsPerWindow = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of partition keys retained concurrently before eviction of idle partitions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1</exception>
    public int MaxPartitions
    {
        get => _maxPartitions;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxPartitions = value;
        }
    }
}
