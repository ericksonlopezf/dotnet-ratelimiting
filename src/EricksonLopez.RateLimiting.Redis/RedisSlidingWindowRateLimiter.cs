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
/// Controls request rates across distributed application instances using a Redis-backed sliding window algorithm.
/// </summary>
/// <remarks>
/// <para>
/// Algorithm: Sliding window using Redis sorted sets (ZSET).
/// Each request is recorded as a ZSET member with its timestamp as the score.
/// The Lua script atomically removes expired entries, counts current entries,
/// and conditionally adds the new request — all in a single round-trip.
/// </para>
/// <para>
/// This implementation is 100% Native AOT compatible (no JSON, no reflection).
/// </para>
/// </remarks>
public sealed class RedisSlidingWindowRateLimiter : IRateLimiter, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger<RedisSlidingWindowRateLimiter> _logger;
    private readonly RedisRateLimiterOptions _options;
    private readonly bool _ownsConnection;

    // Atomic Lua script: sliding window rate limiter using sorted sets.
    // KEYS[1] = rate limit key
    // ARGV[1] = window start timestamp (microseconds)
    // ARGV[2] = current timestamp (microseconds) — used as score
    // ARGV[3] = max permits
    // ARGV[4] = window duration (microseconds)
    // ARGV[5] = number of permits to acquire
    // ARGV[6] = unique request identifier to prevent score/member collisions under high concurrency
    // Returns: { allowed (0/1), remaining_permits, retry_after_us, reset_time_us }
    private const string _slidingWindowLua = """
        local key = KEYS[1]
        local window_start = tonumber(ARGV[1])
        local now = tonumber(ARGV[2])
        local max_permits = tonumber(ARGV[3])
        local window_us = tonumber(ARGV[4])
        local requested = tonumber(ARGV[5])
        local request_id = ARGV[6]

        -- Remove expired entries outside the sliding window
        redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)

        -- Count current entries in the window
        local current = redis.call('ZCARD', key)
        local remaining = max_permits - current

        if remaining >= requested then
            -- Acquire: add entries with unique members (timestamp + request_id + index)
            for i = 1, requested do
                redis.call('ZADD', key, now, now .. ':' .. request_id .. ':' .. i)
            end
            -- Set TTL to window duration (auto-cleanup)
            redis.call('PEXPIRE', key, math.ceil(window_us / 1000))
            remaining = remaining - requested
            return { 1, remaining, 0, now + window_us }
        else
            -- Rejected: compute retry-after from the entry whose expiration satisfies the request
            local needed = requested - remaining
            local oldest = redis.call('ZRANGE', key, 0, -1, 'WITHSCORES')
            local retry_after = 0
            if #oldest >= 2 then
                local target_idx = math.min(needed, math.floor(#oldest / 2)) * 2
                if target_idx >= 2 and target_idx <= #oldest then
                    retry_after = tonumber(oldest[target_idx]) + window_us - now
                    if retry_after < 0 then retry_after = 0 end
                end
            end
            if requested > max_permits then
                retry_after = window_us
            end
            return { 0, 0, retry_after, now + window_us }
        end
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisSlidingWindowRateLimiter"/> class using an externally managed connection multiplexer.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer</param>
    /// <param name="options">The configuration options accessor for the rate limiter</param>
    /// <param name="logger">The logger used to record operational events</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/>, <paramref name="options"/>, or <paramref name="logger"/> is <see langword="null"/></exception>
    public RedisSlidingWindowRateLimiter(
        IConnectionMultiplexer connection,
        IOptions<RedisRateLimiterOptions> options,
        ILogger<RedisSlidingWindowRateLimiter> logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _ownsConnection = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisSlidingWindowRateLimiter"/> class with connection ownership specification.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer</param>
    /// <param name="options">The configuration options for the rate limiter</param>
    /// <param name="logger">The logger used to record operational events</param>
    /// <param name="ownsConnection">A value indicating whether this limiter owns the lifetime of the connection multiplexer</param>
    internal RedisSlidingWindowRateLimiter(
        IConnectionMultiplexer connection,
        RedisRateLimiterOptions options,
        ILogger<RedisSlidingWindowRateLimiter> logger,
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
            var windowUs = (long)_options.WindowDuration.TotalMilliseconds * 1000L;
            var windowStartUs = nowUs - windowUs;
            var requestId = Guid.NewGuid().ToString("N");

            var result = await db.ScriptEvaluateAsync(
                _slidingWindowLua,
                keys: [(RedisKey)fullKey],
                values:
                [
                    windowStartUs,
                    nowUs,
                    _options.MaxPermits,
                    windowUs,
                    permits,
                    requestId
                ]).WaitAsync(cancellationToken).ConfigureAwait(false);

            var values = (RedisResult[])result!;
            var allowed = (int)values[0] == 1;
            var remaining = (int)values[1];
            var retryAfterUs = (long)values[2];
            var resetTimeUs = (long)values[3];
            var durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (allowed)
            {
                RateLimitingMetrics.RecordRequest("redis_sliding_window", "acquired", durationMs);
                Log.AcquireSucceeded(_logger, key, remaining);
                return Result<RateLimitLease>.Success(
                    RateLimitLease.Successful(
                        remaining,
                        DateTimeOffset.FromUnixTimeMilliseconds(resetTimeUs / 1000),
                        limit: _options.MaxPermits));
            }

            var retryAfter = TimeSpan.FromMilliseconds(retryAfterUs / 1000.0);
            RateLimitingMetrics.RecordRequest("redis_sliding_window", "rejected", durationMs);
            Log.AcquireRejected(_logger, key, (long)retryAfter.TotalMilliseconds);
            return Result<RateLimitLease>.Success(
                RateLimitLease.Rejected(
                    retryAfter,
                    DateTimeOffset.FromUnixTimeMilliseconds(resetTimeUs / 1000),
                    limit: _options.MaxPermits));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or System.Net.Sockets.SocketException or ObjectDisposedException or InvalidCastException)
        {
            var durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            RateLimitingMetrics.RecordRequest("redis_sliding_window", "failed", durationMs);
            Log.AcquireFailed(_logger, key, ex);
            return Result<RateLimitLease>.Failure(RateLimitErrors.ConnectionFailed(ex.Message));
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection && _connection is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (_ownsConnection)
        {
            _connection.Dispose();
        }
    }
}
