// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Specifies configuration options for <see cref="ConcurrencyRateLimiter"/>.
/// </summary>
public sealed class ConcurrencyRateLimiterOptions
{
    private int _permitLimit = 10;
    private int _maxPartitions = 10_000;

    /// <summary>
    /// Gets or sets the maximum number of concurrent operations permitted.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1</exception>
    public int PermitLimit
    {
        get => _permitLimit;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "PermitLimit must be at least 1.");
            }
            _permitLimit = value;
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
