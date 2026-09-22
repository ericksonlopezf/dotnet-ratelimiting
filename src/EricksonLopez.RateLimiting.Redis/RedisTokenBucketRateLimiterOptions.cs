// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting.Redis;

/// <summary>
/// Specifies configuration options for the distributed Redis token bucket rate limiter.
/// </summary>
public sealed class RedisTokenBucketRateLimiterOptions
{
    private int _tokenLimit = 100;
    private int _tokensPerPeriod = 10;
    private TimeSpan _replenishmentPeriod = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the connection configuration string used by StackExchange.Redis.
    /// </summary>
    public string Configuration { get; set; } = "localhost:6379,abortConnect=false";

    /// <summary>
    /// Gets or sets the key prefix applied to Redis keys for namespace isolation.
    /// </summary>
    public string KeyPrefix { get; set; } = "rl:tb:";

    /// <summary>
    /// Gets or sets the maximum number of tokens the bucket can hold.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1</exception>
    public int TokenLimit
    {
        get => _tokenLimit;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "TokenLimit must be at least 1.");
            }
            _tokenLimit = value;
        }
    }

    /// <summary>
    /// Gets or sets the number of tokens restored per replenishment period.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1</exception>
    public int TokensPerPeriod
    {
        get => _tokensPerPeriod;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "TokensPerPeriod must be at least 1.");
            }
            _tokensPerPeriod = value;
        }
    }

    /// <summary>
    /// Gets or sets the duration of each replenishment period.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than or equal to <see cref="TimeSpan.Zero"/></exception>
    public TimeSpan ReplenishmentPeriod
    {
        get => _replenishmentPeriod;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "ReplenishmentPeriod must be greater than zero.");
            }
            _replenishmentPeriod = value;
        }
    }

    private int _database;

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
