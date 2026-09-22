// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting.Redis;

/// <summary>
/// Specifies configuration options for the Redis-backed distributed sliding window rate limiter.
/// </summary>
public sealed class RedisRateLimiterOptions
{
    private TimeSpan _windowDuration = TimeSpan.FromMinutes(1);
    private int _maxPermits = 100;
    private int _database;

    /// <summary>
    /// Gets or sets the connection configuration string used by StackExchange.Redis.
    /// </summary>
    public string Configuration { get; set; } = "localhost:6379,abortConnect=false";

    /// <summary>
    /// Gets or sets the key prefix applied to Redis keys for namespace isolation.
    /// </summary>
    public string KeyPrefix { get; set; } = "rl:";

    /// <summary>
    /// Gets or sets the sliding window duration for rate limiting evaluations.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than or equal to <see cref="TimeSpan.Zero"/></exception>
    public TimeSpan WindowDuration
    {
        get => _windowDuration;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "WindowDuration must be greater than zero.");
            }
            _windowDuration = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of permits allowed within the sliding window.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1</exception>
    public int MaxPermits
    {
        get => _maxPermits;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "MaxPermits must be at least 1.");
            }
            _maxPermits = value;
        }
    }

    /// <summary>
    /// Gets or sets the target Redis database index.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 0 or greater than 15</exception>
    public int Database
    {
        get => _database;
        set
        {
            if (value < 0 || value > 15)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Database index must be between 0 and 15.");
            }
            _database = value;
        }
    }
}
