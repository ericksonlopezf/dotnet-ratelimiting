// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EricksonLopez.RateLimiting.Redis.Tests;

public sealed class RedisRateLimitingOptionsAndLoggingTests
{
    [Fact]
    public void RedisRateLimiterOptions_DefaultsAndProperties()
    {
        var options = new RedisRateLimiterOptions();

        options.Configuration.Should().Be("localhost:6379,abortConnect=false");
        options.KeyPrefix.Should().Be("rl:");
        options.WindowDuration.Should().Be(TimeSpan.FromMinutes(1));
        options.MaxPermits.Should().Be(100);
        options.Database.Should().Be(0);

        options.Configuration = "redis.internal:6380";
        options.KeyPrefix = "test:";
        options.WindowDuration = TimeSpan.FromSeconds(30);
        options.MaxPermits = 200;
        options.Database = 2;

        options.Configuration.Should().Be("redis.internal:6380");
        options.KeyPrefix.Should().Be("test:");
        options.WindowDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.MaxPermits.Should().Be(200);
        options.Database.Should().Be(2);
    }

    [Fact]
    public void RedisTokenBucketRateLimiterOptions_DefaultsAndProperties()
    {
        var options = new RedisTokenBucketRateLimiterOptions();

        options.Configuration.Should().Be("localhost:6379,abortConnect=false");
        options.KeyPrefix.Should().Be("rl:tb:");
        options.TokenLimit.Should().Be(100);
        options.TokensPerPeriod.Should().Be(10);
        options.ReplenishmentPeriod.Should().Be(TimeSpan.FromSeconds(1));
        options.Database.Should().Be(0);

        options.TokenLimit = 50;
        options.TokensPerPeriod = 5;
        options.ReplenishmentPeriod = TimeSpan.FromMilliseconds(500);
        options.Configuration = "custom:6379";
        options.KeyPrefix = "custom:tb:";
        options.Database = 1;

        options.TokenLimit.Should().Be(50);
        options.TokensPerPeriod.Should().Be(5);
        options.ReplenishmentPeriod.Should().Be(TimeSpan.FromMilliseconds(500));
        options.Configuration.Should().Be("custom:6379");
        options.KeyPrefix.Should().Be("custom:tb:");
        options.Database.Should().Be(1);
    }

    [Fact]
    public void RedisTokenBucketRateLimiterOptions_Validations()
    {
        var options = new RedisTokenBucketRateLimiterOptions();

        // Boundary tests for valid minimal values
        options.TokenLimit = 1;
        options.TokenLimit.Should().Be(1);

        options.TokensPerPeriod = 1;
        options.TokensPerPeriod.Should().Be(1);

        options.ReplenishmentPeriod = TimeSpan.FromMilliseconds(1);
        options.ReplenishmentPeriod.Should().Be(TimeSpan.FromMilliseconds(1));

        // Invalid boundaries
        Action actTokenLimitZero = () => options.TokenLimit = 0;
        Action actTokenLimitNeg = () => options.TokenLimit = -1;

        Action actTokensPerPeriodZero = () => options.TokensPerPeriod = 0;
        Action actTokensPerPeriodNeg = () => options.TokensPerPeriod = -5;

        Action actReplenishZero = () => options.ReplenishmentPeriod = TimeSpan.Zero;
        Action actReplenishNeg = () => options.ReplenishmentPeriod = TimeSpan.FromSeconds(-1);

        actTokenLimitZero.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*TokenLimit must be at least 1.*");
        actTokenLimitNeg.Should().Throw<ArgumentOutOfRangeException>();

        actTokensPerPeriodZero.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*TokensPerPeriod must be at least 1.*");
        actTokensPerPeriodNeg.Should().Throw<ArgumentOutOfRangeException>();

        actReplenishZero.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*ReplenishmentPeriod must be greater than zero.*");
        actReplenishNeg.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RateLimitErrors_ConnectionFailed_ReturnsExpectedCodeAndDescription()
    {
        var error = RateLimitErrors.ConnectionFailed("timeout on socket");

        error.Code.Should().Be("RateLimit.Redis.ConnectionFailed");
        error.Description.Should().Contain("timeout on socket");
    }

    [Fact]
    public void Log_LoggerMethods_ExecuteWithoutThrowing()
    {
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var act1 = () => Log.AcquireSucceeded(logger, "test-key", 10);
        var act2 = () => Log.AcquireRejected(logger, "test-key", 500);
        var act3 = () => Log.AcquireFailed(logger, "test-key", new InvalidOperationException("boom"));

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
    }
}
