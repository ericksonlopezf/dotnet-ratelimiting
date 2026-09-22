// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.RateLimiting.Redis.Tests;

public sealed class RateLimitingRedisServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddRedisRateLimiting_WithOptionsAction_RegistersServices()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(mockConnection);

        services.AddRedisRateLimiting(opts =>
        {
            opts.MaxPermits = 75;
            opts.KeyPrefix = "app:";
        });

        // Add dummy logging so RedisSlidingWindowRateLimiter can be constructed
        services.AddLogging();

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisRateLimiterOptions>>().Value;
        var limiter = provider.GetService<IRateLimiter>();

        options.MaxPermits.Should().Be(75);
        options.KeyPrefix.Should().Be("app:");
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<RedisSlidingWindowRateLimiter>();
    }

    [Fact]
    public async Task AddRedisRateLimiting_WithExistingConnection_RegistersServices()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        services.AddLogging();

        services.AddRedisRateLimiting(mockConnection, opts =>
        {
            opts.MaxPermits = 50;
        });

        await using var provider = services.BuildServiceProvider();
        var registeredConnection = provider.GetService<IConnectionMultiplexer>();
        var limiter = provider.GetService<IRateLimiter>();
        var options = provider.GetRequiredService<IOptions<RedisRateLimiterOptions>>().Value;

        registeredConnection.Should().BeSameAs(mockConnection);
        options.MaxPermits.Should().Be(50);
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<RedisSlidingWindowRateLimiter>();
    }

    [Fact]
    public async Task AddRedisRateLimiting_WithExistingConnection_NullConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        services.AddLogging();

        services.AddRedisRateLimiting(mockConnection);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisRateLimiterOptions>>().Value;
        options.MaxPermits.Should().Be(100);
    }

    [Fact]
    public async Task AddRedisTokenBucketRateLimiting_WithOptionsAction_RegistersServices()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(mockConnection);
        services.AddLogging();

        services.AddRedisTokenBucketRateLimiting(opts =>
        {
            opts.TokenLimit = 80;
            opts.TokensPerPeriod = 8;
        });

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisTokenBucketRateLimiterOptions>>().Value;
        var limiter = provider.GetService<IRateLimiter>();

        options.TokenLimit.Should().Be(80);
        options.TokensPerPeriod.Should().Be(8);
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<RedisTokenBucketRateLimiter>();
    }

    [Fact]
    public async Task AddRedisTokenBucketRateLimiting_WithExistingConnection_RegistersServices()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        services.AddLogging();

        services.AddRedisTokenBucketRateLimiting(mockConnection, opts =>
        {
            opts.TokenLimit = 40;
        });

        await using var provider = services.BuildServiceProvider();
        var registeredConnection = provider.GetService<IConnectionMultiplexer>();
        var limiter = provider.GetService<IRateLimiter>();
        var options = provider.GetRequiredService<IOptions<RedisTokenBucketRateLimiterOptions>>().Value;

        registeredConnection.Should().BeSameAs(mockConnection);
        options.TokenLimit.Should().Be(40);
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<RedisTokenBucketRateLimiter>();
    }

    [Fact]
    public async Task AddRedisTokenBucketRateLimiting_WithExistingConnection_NullConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<IConnectionMultiplexer>();
        services.AddLogging();

        services.AddRedisTokenBucketRateLimiting(mockConnection);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisTokenBucketRateLimiterOptions>>().Value;
        options.TokenLimit.Should().Be(100);
    }

    [Fact]
    public void AddRedisRateLimiting_RegistersConnectionFactoryDescriptor_FactoryInvokesConnect()
    {
        var services = new ServiceCollection();
        services.AddRedisRateLimiting(opts =>
        {
            opts.Configuration = "127.0.0.1:1,abortConnect=true,connectTimeout=50";
        });

        var descriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(IConnectionMultiplexer));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationFactory.Should().NotBeNull();

        var limiterDescriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(IRateLimiter));
        limiterDescriptor.Should().NotBeNull();
        limiterDescriptor!.ImplementationType.Should().Be<RedisSlidingWindowRateLimiter>();

        using var sp = services.BuildServiceProvider();
        var act = () => descriptor.ImplementationFactory!(sp);
        act.Should().Throw<RedisConnectionException>();
    }

    [Fact]
    public void AddRedisTokenBucketRateLimiting_RegistersConnectionFactoryDescriptor_FactoryInvokesConnect()
    {
        var services = new ServiceCollection();
        services.AddRedisTokenBucketRateLimiting(opts =>
        {
            opts.Configuration = "127.0.0.1:1,abortConnect=true,connectTimeout=50";
        });

        var descriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(IConnectionMultiplexer));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationFactory.Should().NotBeNull();

        var limiterDescriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(IRateLimiter));
        limiterDescriptor.Should().NotBeNull();
        limiterDescriptor!.ImplementationType.Should().Be<RedisTokenBucketRateLimiter>();

        using var sp = services.BuildServiceProvider();
        var act = () => descriptor.ImplementationFactory!(sp);
        act.Should().Throw<RedisConnectionException>();
    }

    [Fact]
    public void AddRedisRateLimiting_WithExistingConnection_RegistersSingletonInstance()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<IConnectionMultiplexer>();

        services.AddRedisRateLimiting(mockConnection);

        var descriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(IConnectionMultiplexer));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationInstance.Should().BeSameAs(mockConnection);
    }

    [Fact]
    public void AddRedisTokenBucketRateLimiting_WithExistingConnection_RegistersSingletonInstance()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<IConnectionMultiplexer>();

        services.AddRedisTokenBucketRateLimiting(mockConnection);

        var descriptor = System.Linq.Enumerable.FirstOrDefault(services, d => d.ServiceType == typeof(IConnectionMultiplexer));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationInstance.Should().BeSameAs(mockConnection);
    }

    [Fact]
    public void GuardClauses_ThrowArgumentNullException()
    {
        IServiceCollection nullServices = null!;
        var mockConnection = Substitute.For<IConnectionMultiplexer>();

        var ex1 = Assert.Throws<ArgumentNullException>(() => nullServices.AddRedisRateLimiting(_ => { }));
        ex1.ParamName.Should().Be("services");

        var ex2 = Assert.Throws<ArgumentNullException>(() => nullServices.AddRedisRateLimiting(mockConnection));
        ex2.ParamName.Should().Be("services");

        var ex3 = Assert.Throws<ArgumentNullException>(() => nullServices.AddRedisTokenBucketRateLimiting(_ => { }));
        ex3.ParamName.Should().Be("services");

        var ex4 = Assert.Throws<ArgumentNullException>(() => nullServices.AddRedisTokenBucketRateLimiting(mockConnection));
        ex4.ParamName.Should().Be("services");

        var validServices = new ServiceCollection();
        var ex5 = Assert.Throws<ArgumentNullException>(() => validServices.AddRedisRateLimiting((Action<RedisRateLimiterOptions>)null!));
        ex5.ParamName.Should().Be("configure");

        var ex6 = Assert.Throws<ArgumentNullException>(() => validServices.AddRedisRateLimiting((IConnectionMultiplexer)null!));
        ex6.ParamName.Should().Be("connectionMultiplexer");

        var ex7 = Assert.Throws<ArgumentNullException>(() => validServices.AddRedisTokenBucketRateLimiting((Action<RedisTokenBucketRateLimiterOptions>)null!));
        ex7.ParamName.Should().Be("configure");

        var ex8 = Assert.Throws<ArgumentNullException>(() => validServices.AddRedisTokenBucketRateLimiting((IConnectionMultiplexer)null!));
        ex8.ParamName.Should().Be("connectionMultiplexer");
    }
}
