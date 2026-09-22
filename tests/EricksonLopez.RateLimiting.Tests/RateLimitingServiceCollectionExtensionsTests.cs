// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class RateLimitingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSlidingWindowRateLimiter_RegistersServicesProperly()
    {
        var services = new ServiceCollection();
        services.AddSlidingWindowRateLimiter(opts => opts.PermitLimit = 42);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<RateLimiterOptions>();
        var limiter = provider.GetService<IRateLimiter>();

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(42);
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<SlidingWindowRateLimiter>();
    }

    [Fact]
    public void AddSlidingWindowRateLimiter_NullConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddSlidingWindowRateLimiter();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<RateLimiterOptions>();
        var limiter = provider.GetService<IRateLimiter>();

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(100);
        limiter.Should().BeOfType<SlidingWindowRateLimiter>();
    }

    [Fact]
    public void AddTokenBucketRateLimiter_RegistersServicesProperly()
    {
        var services = new ServiceCollection();
        services.AddTokenBucketRateLimiter(opts => opts.PermitLimit = 55);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<RateLimiterOptions>();
        var limiter = provider.GetService<IRateLimiter>();

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(55);
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<TokenBucketRateLimiter>();
    }

    [Fact]
    public void AddTokenBucketRateLimiter_NullConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddTokenBucketRateLimiter();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<RateLimiterOptions>();
        var limiter = provider.GetService<IRateLimiter>();

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(100);
        limiter.Should().BeOfType<TokenBucketRateLimiter>();
    }

    [Fact]
    public void AddFixedWindowRateLimiter_RegistersServicesProperly()
    {
        var services = new ServiceCollection();
        services.AddFixedWindowRateLimiter(opts => opts.PermitLimit = 25);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<RateLimiterOptions>();
        var limiter = provider.GetService<IRateLimiter>();

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(25);
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<FixedWindowRateLimiter>();
    }

    [Fact]
    public void AddFixedWindowRateLimiter_NullConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddFixedWindowRateLimiter();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<RateLimiterOptions>();
        var limiter = provider.GetService<IRateLimiter>();

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(100);
        limiter.Should().BeOfType<FixedWindowRateLimiter>();
    }

    [Fact]
    public void AddConcurrencyRateLimiter_RegistersServicesProperly()
    {
        var services = new ServiceCollection();
        services.AddConcurrencyRateLimiter(opts => opts.PermitLimit = 8);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<ConcurrencyRateLimiterOptions>();
        var limiter = provider.GetService<IRateLimiter>();

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(8);
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<ConcurrencyRateLimiter>();
    }

    [Fact]
    public void AddConcurrencyRateLimiter_NullConfigure_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddConcurrencyRateLimiter();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<ConcurrencyRateLimiterOptions>();
        var limiter = provider.GetService<IRateLimiter>();

        options.Should().NotBeNull();
        options!.PermitLimit.Should().Be(10);
        limiter.Should().BeOfType<ConcurrencyRateLimiter>();
    }

    [Fact]
    public void AddCompositeRateLimiter_RegistersCompositeProperly()
    {
        var services = new ServiceCollection();
        var l1 = new FixedWindowRateLimiter();
        var l2 = new SlidingWindowRateLimiter();

        services.AddCompositeRateLimiter(l1, l2);

        using var provider = services.BuildServiceProvider();
        var limiter = provider.GetService<IRateLimiter>();

        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<CompositeRateLimiter>();
        var composite = (CompositeRateLimiter)limiter!;
        composite.Limiters.Should().HaveCount(2);
    }

    [Fact]
    public void GuardClauses_ThrowArgumentNullException()
    {
        IServiceCollection nullServices = null!;

        Assert.Throws<ArgumentNullException>(() => nullServices.AddSlidingWindowRateLimiter());
        Assert.Throws<ArgumentNullException>(() => nullServices.AddTokenBucketRateLimiter());
        Assert.Throws<ArgumentNullException>(() => nullServices.AddFixedWindowRateLimiter());
        Assert.Throws<ArgumentNullException>(() => nullServices.AddConcurrencyRateLimiter());
        Assert.Throws<ArgumentNullException>(() => nullServices.AddCompositeRateLimiter(new FixedWindowRateLimiter()));

        var validServices = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => validServices.AddCompositeRateLimiter(null!));
    }
}
