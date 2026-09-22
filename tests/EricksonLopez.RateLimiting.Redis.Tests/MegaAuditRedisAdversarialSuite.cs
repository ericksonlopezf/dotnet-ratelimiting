// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.RateLimiting.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.RateLimiting.Redis.Tests;

public sealed class MegaAuditRedisAdversarialSuite
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();

    public MegaAuditRedisAdversarialSuite()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void RedisTokenBucketRateLimiterOptions_Database_MustValidateRangeZeroToFifteen(int invalidDb)
    {
        var options = new RedisTokenBucketRateLimiterOptions();
        Action act = () => options.Database = invalidDb;
        act.Should().Throw<ArgumentOutOfRangeException>(
            "Redis database indices must be strictly within 0 to 15");
    }

    [Fact]
    public async Task RedisSlidingWindow_MidFlightCancellation_MustThrowOperationCanceledException()
    {
        // When Redis call is slow or network hung, cancelling token while awaiting Redis must throw OperationCanceledException
        var tcs = new TaskCompletionSource<RedisResult>();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(tcs.Task);

        var options = new RedisRateLimiterOptions
        {
            KeyPrefix = "test:",
            MaxPermits = 10,
            WindowDuration = TimeSpan.FromMinutes(1)
        };

        var limiter = new RedisSlidingWindowRateLimiter(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisSlidingWindowRateLimiter>.Instance);

        using var cts = new CancellationTokenSource();
        var acquireTask = limiter.AcquireAsync("hung-key", 1, cts.Token);

        // Cancel mid-flight
        cts.Cancel();

        Func<Task> act = async () => await acquireTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RedisTokenBucket_MidFlightCancellation_MustThrowOperationCanceledException()
    {
        var tcs = new TaskCompletionSource<RedisResult>();
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(tcs.Task);

        var options = new RedisTokenBucketRateLimiterOptions
        {
            KeyPrefix = "test:tb:",
            TokenLimit = 10,
            TokensPerPeriod = 2,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1)
        };

        var limiter = new RedisTokenBucketRateLimiter(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisTokenBucketRateLimiter>.Instance);

        using var cts = new CancellationTokenSource();
        var acquireTask = limiter.AcquireAsync("hung-key", 1, cts.Token);

        cts.Cancel();

        Func<Task> act = async () => await acquireTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
