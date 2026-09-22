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

public sealed class RedisMultiPermitAdversarialTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();

    public RedisMultiPermitAdversarialTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
    }

    [Fact]
    public async Task RedisSlidingWindow_MultiPermitAcquire_PropagatesPermitsToLuaScript()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue[] { 1, 5, 0, 1725500000000000L })));

        var options = new RedisRateLimiterOptions
        {
            KeyPrefix = "multi:",
            MaxPermits = 10,
            WindowDuration = TimeSpan.FromMinutes(1)
        };

        var limiter = new RedisSlidingWindowRateLimiter(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisSlidingWindowRateLimiter>.Instance);

        var result = await limiter.AcquireAsync("tenant-batch", 5);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(5);
        result.Value.Limit.Should().Be(10);
    }

    [Fact]
    public async Task RedisTokenBucket_MultiPermitAcquire_PropagatesPermitsToLuaScript()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(new RedisValue[] { 1, 2, 0, 1725500000000000L })));

        var options = new RedisTokenBucketRateLimiterOptions
        {
            KeyPrefix = "multi-tb:",
            TokenLimit = 10,
            TokensPerPeriod = 5,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1)
        };

        var limiter = new RedisTokenBucketRateLimiter(
            _multiplexer,
            Options.Create(options),
            NullLogger<RedisTokenBucketRateLimiter>.Instance);

        var result = await limiter.AcquireAsync("tenant-batch", 8);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAcquired.Should().BeTrue();
        result.Value.RemainingPermits.Should().Be(2);
        result.Value.Limit.Should().Be(10);
    }
}
