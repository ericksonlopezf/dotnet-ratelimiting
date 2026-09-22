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
/// Unit tests for <see cref="RedisTokenBucketRateLimiter"/> using a mocked <see cref="IConnectionMultiplexer"/>.
/// </summary>
public sealed class RedisTokenBucketRateLimiterTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisTokenBucketRateLimiterOptions _options = new()
    {
        KeyPrefix = "rl:tb:",
        TokenLimit = 100,
        TokensPerPeriod = 10,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        Database = 0
    };

    private RedisTokenBucketRateLimiter CreateLimiter()
    {
        _multiplexer.GetDatabase(_options.Database).Returns(_database);
        return new RedisTokenBucketRateLimiter(
            _multiplexer,
            Options.Create(_options),
            NullLogger<RedisTokenBucketRateLimiter>.Instance);
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
        var limiter = CreateLimiter();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resetTimeUs = (nowMs + 1_000L) * 1000L;
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(BuildLuaResult(1, 99, 0, resetTimeUs));

        var result = await limiter.AcquireAsync("client:user1");

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(99);
        result.Value.RetryAfter.Should().BeNull();

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(k => k.Length == 1 && k[0].ToString() == "rl:tb:client:user1"),
            Arg.Is<RedisValue[]>(args =>
                args.Length == 5 &&
                (long)args[0] > 1_000_000_000_000_000L &&
                (long)args[1] == 100L &&
                (long)args[2] == 10L &&
                (long)args[3] == 1_000_000L &&
                (long)args[4] == 1L),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task AcquireAsync_WhenRateLimited_ReturnsRejectedLease()
    {
        var limiter = CreateLimiter();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resetTimeUs = (nowMs + 1_000L) * 1000L;
        var retryAfterUs = 2_000_000L; // 2 seconds
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(BuildLuaResult(0, 0, retryAfterUs, resetTimeUs));

        var result = await limiter.AcquireAsync("client:user1");

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeFalse();
        result.Value.RemainingPermits.Should().Be(0);
        result.Value.RetryAfter.Should().NotBeNull();
        result.Value.RetryAfter!.Value.TotalSeconds.Should().BeApproximately(2.0, 0.1);
    }

    [Fact]
    public async Task AcquireAsync_WhenRedisThrows_ReturnsConnectionFailedError()
    {
        var limiter = CreateLimiter();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Redis offline"));

        var result = await limiter.AcquireAsync("client:user1");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RateLimit.Redis.ConnectionFailed");
    }

    [Fact]
    public async Task AcquireAsync_WithNegativePermits_ThrowsArgumentOutOfRangeException()
    {
        var limiter = CreateLimiter();

        Func<Task> act = async () => await limiter.AcquireAsync("key", -1);

        var ex = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        ex.Which.Message.Should().Contain("Permits must be at least 1.");
    }

    [Fact]
    public async Task AcquireAsync_WithNullKey_ThrowsArgumentNullException()
    {
        var limiter = CreateLimiter();

        Func<Task> act = async () => await limiter.AcquireAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WhenConnectionIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisTokenBucketRateLimiter(
                null!,
                Options.Create(_options),
                NullLogger<RedisTokenBucketRateLimiter>.Instance));
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisTokenBucketRateLimiter(
                _multiplexer,
                null!,
                NullLogger<RedisTokenBucketRateLimiter>.Instance));
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisTokenBucketRateLimiter(
                _multiplexer,
                Options.Create(_options),
                null!));
    }

    [Fact]
    public void Constructor_Internal_InitializesInstance()
    {
        var limiter = new RedisTokenBucketRateLimiter(
            _multiplexer,
            _options,
            NullLogger<RedisTokenBucketRateLimiter>.Instance,
            ownsConnection: true);

        limiter.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_WhenDoesNotOwnConnection_DoesNotDisposeConnection()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer, IAsyncDisposable>();
        var limiter = new RedisTokenBucketRateLimiter(
            multiplexer,
            Options.Create(_options),
            NullLogger<RedisTokenBucketRateLimiter>.Instance);

        await limiter.DisposeAsync();

        await ((IAsyncDisposable)multiplexer).DidNotReceive().DisposeAsync();
        multiplexer.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task DisposeAsync_WhenOwnsConnection_DisposesAsync()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var limiter = new RedisTokenBucketRateLimiter(
            multiplexer,
            _options,
            NullLogger<RedisTokenBucketRateLimiter>.Instance,
            ownsConnection: true);

        await limiter.DisposeAsync();

        await multiplexer.Received(1).DisposeAsync();
    }
}
