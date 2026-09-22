// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EricksonLopez.RateLimiting.Redis;

/// <summary>
/// Controls request rates across distributed application instances using a Redis-backed token bucket algorithm.
/// </summary>
/// <remarks>
/// This rate limiter executes an atomic Lua script over Redis hashes to continuously replenish tokens and compute quotas in a single round-trip.
/// </remarks>
public sealed class RedisTokenBucketRateLimiter : IRateLimiter, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger<RedisTokenBucketRateLimiter> _logger;
    private readonly RedisTokenBucketRateLimiterOptions _options;
    private readonly bool _ownsConnection;

    // Atomic Lua script: token bucket rate limiter using a hash.
    // KEYS[1] = rate limit key
    // ARGV[1] = current timestamp (microseconds)
    // ARGV[2] = max tokens (capacity)
    // ARGV[3] = tokens replenished per period
    // ARGV[4] = replenishment period duration (microseconds)
    // ARGV[5] = requested permits
    // Returns: { allowed (0/1), remaining_tokens, retry_after_us, reset_time_us }
    private const string _tokenBucketLua = """
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local max_tokens = tonumber(ARGV[2])
        local tokens_per_period = tonumber(ARGV[3])
        local period_us = tonumber(ARGV[4])
        local requested = tonumber(ARGV[5])

        if period_us <= 0 then period_us = 1000000 end
        if tokens_per_period <= 0 then tokens_per_period = 1 end

        local data = redis.call('HMGET', key, 'tokens', 'last_updated')
        local tokens = tonumber(data[1])
        local last_updated = tonumber(data[2])

        local fill_rate = tokens_per_period / period_us
        if fill_rate <= 0 then fill_rate = 0.000001 end

        if not tokens or not last_updated then
            tokens = max_tokens
            last_updated = now
        else
            local elapsed = now - last_updated
            if elapsed > 0 then
                local generated = elapsed * fill_rate
                tokens = math.min(max_tokens, tokens + generated)
                last_updated = now
            end
        end

        local allowed = 0
        local remaining = 0
        local retry_after_us = 0

        if tokens >= requested then
            allowed = 1
            tokens = tokens - requested
            remaining = math.floor(tokens)
        else
            allowed = 0
            remaining = math.floor(tokens)
            if requested > max_tokens then
                local full_refill_us = (max_tokens / tokens_per_period) * period_us
                retry_after_us = math.ceil(full_refill_us)
            else
                local needed = requested - tokens
                retry_after_us = math.ceil(needed / fill_rate)
            end
        end

        redis.call('HSET', key, 'tokens', tostring(tokens), 'last_updated', tostring(last_updated))

        local full_refill_us = (max_tokens / tokens_per_period) * period_us
        local ttl_ms = math.ceil(full_refill_us / 1000) * 2
        if ttl_ms < 60000 then ttl_ms = 60000 end
        redis.call('PEXPIRE', key, ttl_ms)

        return { allowed, remaining, retry_after_us, now + period_us }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisTokenBucketRateLimiter"/> class using an externally managed Redis connection multiplexer.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer</param>
    /// <param name="options">The configuration options accessor for the rate limiter</param>
    /// <param name="logger">The logger used to record operational events</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/>, <paramref name="options"/>, or <paramref name="logger"/> is <see langword="null"/></exception>
    public RedisTokenBucketRateLimiter(
        IConnectionMultiplexer connection,
        IOptions<RedisTokenBucketRateLimiterOptions> options,
        ILogger<RedisTokenBucketRateLimiter> logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _ownsConnection = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisTokenBucketRateLimiter"/> class with connection ownership specification.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer</param>
    /// <param name="options">The configuration options for the rate limiter</param>
    /// <param name="logger">The logger used to record operational events</param>
    /// <param name="ownsConnection">A value indicating whether this limiter owns the lifetime of the connection multiplexer</param>
    internal RedisTokenBucketRateLimiter(
        IConnectionMultiplexer connection,
        RedisTokenBucketRateLimiterOptions options,
        ILogger<RedisTokenBucketRateLimiter> logger,
        bool ownsConnection)
    {
        _connection = connection;
        _logger = logger;
        _options = options;
        _ownsConnection = ownsConnection;
    }

    /// <inheritdoc/>
    public async Task<Result<RateLimitLease>> AcquireAsync(
        string key,
        int permits = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        if (permits < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(permits), "Permits must be at least 1.");
        }

        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            var db = _connection.GetDatabase(_options.Database);
            var fullKey = $"{_options.KeyPrefix}{key}";

            var nowUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            var periodUs = (long)_options.ReplenishmentPeriod.TotalMilliseconds * 1000L;

            var result = await db.ScriptEvaluateAsync(
                _tokenBucketLua,
                keys: [(RedisKey)fullKey],
                values:
                [
                    nowUs,
                    _options.TokenLimit,
                    _options.TokensPerPeriod,
                    periodUs,
                    permits
                ]).WaitAsync(cancellationToken).ConfigureAwait(false);

            var values = (RedisResult[])result!;
            var allowed = (int)values[0] == 1;
            var remaining = (int)values[1];
            var retryAfterUs = (long)values[2];
            var resetTimeUs = (long)values[3];
            var durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (allowed)
            {
                RateLimitingMetrics.RecordRequest("redis_token_bucket", "acquired", durationMs);
                Log.AcquireSucceeded(_logger, key, remaining);
                return Result<RateLimitLease>.Success(
                    RateLimitLease.Successful(
                        remaining,
                        DateTimeOffset.FromUnixTimeMilliseconds(resetTimeUs / 1000),
                        limit: _options.TokenLimit));
            }

            var retryAfter = TimeSpan.FromMilliseconds(retryAfterUs / 1000.0);
            RateLimitingMetrics.RecordRequest("redis_token_bucket", "rejected", durationMs);
            Log.AcquireRejected(_logger, key, (long)retryAfter.TotalMilliseconds);
            return Result<RateLimitLease>.Success(
                RateLimitLease.Rejected(
                    retryAfter,
                    DateTimeOffset.FromUnixTimeMilliseconds(resetTimeUs / 1000),
                    limit: _options.TokenLimit));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or System.Net.Sockets.SocketException or ObjectDisposedException or InvalidCastException)
        {
            var durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            RateLimitingMetrics.RecordRequest("redis_token_bucket", "failed", durationMs);
            Log.AcquireFailed(_logger, key, ex);
            return Result<RateLimitLease>.Failure(RateLimitErrors.ConnectionFailed(ex.Message));
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
