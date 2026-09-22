// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.RateLimiting.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.RateLimiting.Redis.Tests;

/// <summary>
/// Unit tests for <see cref="RedisSlidingWindowRateLimiter"/> using a mocked <see cref="IConnectionMultiplexer"/>.
/// </summary>
public sealed class RedisSlidingWindowRateLimiterTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisRateLimiterOptions _options = new()
    {
        KeyPrefix = "rl:",
        MaxPermits = 100,
        WindowDuration = TimeSpan.FromMinutes(1),
        Database = 0
    };

    private RedisSlidingWindowRateLimiter CreateLimiter()
    {
        _multiplexer.GetDatabase(_options.Database).Returns(_database);
        return new RedisSlidingWindowRateLimiter(
            _multiplexer,
            Options.Create(_options),
            NullLogger<RedisSlidingWindowRateLimiter>.Instance);
    }

    private static RedisResult BuildLuaResult(int allowed, int remaining, long retryAfterUs, long resetTimeUs)
    {
        return RedisResult.Create(
        [
            RedisResult.Create((RedisValue)(long)allowed),
            RedisResult.Create((RedisValue)(long)remaining),
            RedisResult.Create((RedisValue)retryAfterUs),
            RedisResult.Create((RedisValue)resetTimeUs),
        ]);
    }

    [Fact]
    public async Task AcquireAsync_WhenPermitted_ReturnsSuccessfulLease()
    {
        // Arrange
        var limiter = CreateLimiter();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resetTimeUs = (nowMs + 60_000L) * 1000L;
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(BuildLuaResult(1, 99, 0, resetTimeUs));

        // Act
        var result = await limiter.AcquireAsync("client:user1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(99);
        result.Value.RetryAfter.Should().BeNull();

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Is<RedisValue[]>(args =>
                args.Length == 6 &&
                (long)args[0] == (long)args[1] - (long)args[3] &&
                (long)args[1] > 1_000_000_000_000_000L &&
                (long)args[2] == 100L &&
                (long)args[3] == 60_000_000L &&
                (long)args[4] == 1L &&
                !string.IsNullOrEmpty((string)args[5]!)),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task AcquireAsync_WhenRateLimited_ReturnsRejectedLease()
    {
        // Arrange
        var limiter = CreateLimiter();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resetTimeUs = (nowMs + 60_000L) * 1000L;
        var retryAfterUs = 5_000_000L; // 5 seconds
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(BuildLuaResult(0, 0, retryAfterUs, resetTimeUs));

        // Act
        var result = await limiter.AcquireAsync("client:user1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeFalse();
        result.Value.RemainingPermits.Should().Be(0);
        result.Value.RetryAfter.Should().NotBeNull();
        result.Value.RetryAfter!.Value.TotalSeconds.Should().BeApproximately(5.0, 0.1);
    }

    [Fact]
    public async Task AcquireAsync_WhenRedisThrows_ReturnsFailure()
    {
        // Arrange
        var limiter = CreateLimiter();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Throws(new RedisException("cluster down"));

        // Act
        var result = await limiter.AcquireAsync("client:user1");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RateLimit.Redis.ConnectionFailed");
    }

    [Fact]
    public async Task AcquireAsync_WhenPermits_LessThanOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            limiter.AcquireAsync("client:user1", permits: 0));
        ex.Message.Should().Contain("Permits must be at least 1.");
    }

    [Fact]
    public async Task AcquireAsync_WhenNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            limiter.AcquireAsync(null!));
    }

    [Fact]
    public async Task AcquireAsync_KeyIsPrefixed_WithConfiguredPrefix()
    {
        // Arrange
        var limiter = CreateLimiter();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(BuildLuaResult(1, 99, 0, (nowMs + 60_000L) * 1000L));

        // Act
        await limiter.AcquireAsync("my-client");

        // Assert — the key sent to Redis must include the configured prefix
        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0].ToString() == "rl:my-client"),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task AcquireAsync_MultiplePermits_PassedToLuaScript()
    {
        // Arrange
        var limiter = CreateLimiter();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(BuildLuaResult(1, 95, 0, (nowMs + 60_000L) * 1000L));

        // Act
        var result = await limiter.AcquireAsync("client:batch", permits: 5);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();

        // Verify the permits value was passed as ARGV[5]
        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Is<RedisValue[]>(args => args.Length == 6 && (long)args[4] == 5L),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task AcquireAsync_WhenPermitted_ResetsTimeIsPopulated()
    {
        // Arrange
        var limiter = CreateLimiter();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expectedResetMs = nowMs + 60_000L;
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(BuildLuaResult(1, 50, 0, expectedResetMs * 1000L));

        // Act
        var result = await limiter.AcquireAsync("client:x");

        // Assert
        result.Value.ResetTime.Should().NotBeNull();
        var actualMs = result.Value.ResetTime!.Value.ToUnixTimeMilliseconds();
        actualMs.Should().BeGreaterThanOrEqualTo(expectedResetMs - 1L);
        actualMs.Should().BeLessThanOrEqualTo(expectedResetMs + 1L);
    }

    [Fact]
    public void Constructor_WhenConnectionIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisSlidingWindowRateLimiter(
                null!,
                Options.Create(_options),
                NullLogger<RedisSlidingWindowRateLimiter>.Instance));
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisSlidingWindowRateLimiter(
                _multiplexer,
                null!,
                NullLogger<RedisSlidingWindowRateLimiter>.Instance));
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisSlidingWindowRateLimiter(
                _multiplexer,
                Options.Create(_options),
                null!));
    }

    [Fact]
    public void Constructor_Internal_InitializesInstance()
    {
        var limiter = new RedisSlidingWindowRateLimiter(
            _multiplexer,
            _options,
            NullLogger<RedisSlidingWindowRateLimiter>.Instance,
            ownsConnection: true);

        limiter.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_WhenDoesNotOwnConnection_DoesNotDisposeConnection()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer, IAsyncDisposable>();
        var limiter = new RedisSlidingWindowRateLimiter(
            multiplexer,
            Options.Create(_options),
            NullLogger<RedisSlidingWindowRateLimiter>.Instance);

        await limiter.DisposeAsync();

        await ((IAsyncDisposable)multiplexer).DidNotReceive().DisposeAsync();
        multiplexer.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task DisposeAsync_WhenOwnsConnection_DisposesAsync()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var limiter = new RedisSlidingWindowRateLimiter(
            multiplexer,
            _options,
            NullLogger<RedisSlidingWindowRateLimiter>.Instance,
            ownsConnection: true);

        await limiter.DisposeAsync();

        await multiplexer.Received(1).DisposeAsync();
    }
}
