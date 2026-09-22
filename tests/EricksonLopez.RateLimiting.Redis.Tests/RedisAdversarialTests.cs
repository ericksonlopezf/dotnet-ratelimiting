// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Net.Sockets;
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

public sealed class RedisAdversarialTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisRateLimiterOptions _options = new()
    {
        KeyPrefix = "test:",
        MaxPermits = 10,
        WindowDuration = TimeSpan.FromMinutes(1)
    };

    public RedisAdversarialTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
    }

    [Fact]
    public async Task Attack_CancellationRequested_MustThrowOperationCanceledExceptionAndNotHitRedis()
    {
        // Vector: Calling AcquireAsync with an already cancelled CancellationToken.
        // In the current flawed code, the token is completely ignored, and Redis is queried anyway!
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var limiter = new RedisSlidingWindowRateLimiter(
            _multiplexer,
            Options.Create(_options),
            NullLogger<RedisSlidingWindowRateLimiter>.Instance);

        Func<Task> act = () => limiter.AcquireAsync("test-key", 1, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "AcquireAsync must respect the cancellation token and not execute remote Redis operations when cancelled!");

        await _database.DidNotReceive().ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task Attack_SocketException_MustReturnFailureNotCrash()
    {
        // Vector: Redis connection drops and StackExchange.Redis throws SocketException or TimeoutException (not RedisException).
        // In the flawed code, only RedisException is caught, causing an unhandled crash!
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Throws(new TimeoutException("Redis socket timed out"));

        var limiter = new RedisSlidingWindowRateLimiter(
            _multiplexer,
            Options.Create(_options),
            NullLogger<RedisSlidingWindowRateLimiter>.Instance);

        var result = await limiter.AcquireAsync("test-key", 1);

        result.IsFailure.Should().BeTrue("non-RedisException infrastructure errors must be captured into Result.Failure");
        result.Error.Code.Should().Be("RateLimit.Redis.ConnectionFailed");
    }

    [Fact]
    public async Task Attack_TokenBucket_SocketException_MustReturnFailureNotCrash()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Throws(new TimeoutException("Redis socket timed out"));

        var tbOptions = new RedisTokenBucketRateLimiterOptions
        {
            KeyPrefix = "test:tb:",
            TokenLimit = 10,
            TokensPerPeriod = 2,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1)
        };

        var limiter = new RedisTokenBucketRateLimiter(
            _multiplexer,
            Options.Create(tbOptions),
            NullLogger<RedisTokenBucketRateLimiter>.Instance);

        var result = await limiter.AcquireAsync("test-key", 1);

        result.IsFailure.Should().BeTrue("non-RedisException infrastructure errors must be captured into Result.Failure");
        result.Error.Code.Should().Be("RateLimit.Redis.ConnectionFailed");
    }

    [Fact]
    public void RedisRateLimiterOptions_InvalidOptions_MustThrowArgumentOutOfRangeException()
    {
        // Vector: Attacker or misconfiguration sets MaxPermits <= 0 or WindowDuration <= 0
        var options = new RedisRateLimiterOptions();

        Action actZeroPermits = () => options.MaxPermits = 0;
        Action actNegPermits = () => options.MaxPermits = -5;
        Action actZeroWindow = () => options.WindowDuration = TimeSpan.Zero;
        Action actNegWindow = () => options.WindowDuration = TimeSpan.FromSeconds(-1);
        Action actNegDb = () => options.Database = -1;

        actZeroPermits.Should().Throw<ArgumentOutOfRangeException>();
        actNegPermits.Should().Throw<ArgumentOutOfRangeException>();
        actZeroWindow.Should().Throw<ArgumentOutOfRangeException>();
        actNegWindow.Should().Throw<ArgumentOutOfRangeException>();
        actNegDb.Should().Throw<ArgumentOutOfRangeException>();
    }
}
