// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.RateLimiting.Policies;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class RateLimiterPolicyTests
{
    [Fact]
    public void RateLimiterPolicy_ValidParameters_InitializesCorrectly()
    {
        var limiter = new FixedWindowRateLimiter();
        var policy = new RateLimiterPolicy("tier-1", limiter);

        policy.Name.Should().Be("tier-1");
        policy.Limiter.Should().BeSameAs(limiter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RateLimiterPolicy_InvalidName_ThrowsArgumentException(string? invalidName)
    {
        var limiter = new FixedWindowRateLimiter();

        var act = () => new RateLimiterPolicy(invalidName!, limiter);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RateLimiterPolicy_NullLimiter_ThrowsArgumentNullException()
    {
        var act = () => new RateLimiterPolicy("tier-1", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Registry_RegisterAndGet_IsCaseInsensitive()
    {
        var registry = new RateLimiterPolicyRegistry();
        var limiter = new FixedWindowRateLimiter();

        registry.Register("API_CLIENT", limiter);

        registry.GetPolicy("api_client").Should().BeSameAs(limiter);
        registry.GetPolicy("API_CLIENT").Should().BeSameAs(limiter);
        registry.GetPolicy("unknown").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Registry_Register_InvalidName_ThrowsArgumentException(string? invalidName)
    {
        var registry = new RateLimiterPolicyRegistry();
        var limiter = new FixedWindowRateLimiter();

        var act = () => registry.Register(invalidName!, limiter);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Registry_Register_NullLimiter_ThrowsArgumentNullException()
    {
        var registry = new RateLimiterPolicyRegistry();

        var act = () => registry.Register("policy", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Registry_GetPolicy_InvalidName_ThrowsArgumentException(string? invalidName)
    {
        var registry = new RateLimiterPolicyRegistry();

        var act = () => registry.GetPolicy(invalidName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Registry_DefaultLimiter_GetAndSet()
    {
        var registry = new RateLimiterPolicyRegistry();
        registry.DefaultLimiter.Should().BeNull();

        var limiter = new FixedWindowRateLimiter();
        registry.DefaultLimiter = limiter;

        registry.DefaultLimiter.Should().BeSameAs(limiter);
    }

    [Fact]
    public async Task Builder_AddConcurrency_RegistersPolicyAndAppliesCustomOptions()
    {
        var configured = false;
        var builder = new RateLimiterPolicyBuilder();
        builder.AddConcurrency("concurrency-policy", opts =>
        {
            opts.PermitLimit = 1;
            configured = true;
        });

        var registry = builder.Build();
        var limiter = registry.GetPolicy("concurrency-policy");

        configured.Should().BeTrue();
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<ConcurrencyRateLimiter>();

        using var lease1 = (await limiter!.AcquireAsync("tenant-1", 1)).Value;
        lease1.IsAcquired.Should().BeTrue();

        var lease2 = (await limiter.AcquireAsync("tenant-1", 1)).Value;
        lease2.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void Builder_AddComposite_RegistersPolicy()
    {
        var builder = new RateLimiterPolicyBuilder();
        var l1 = new FixedWindowRateLimiter();
        var l2 = new SlidingWindowRateLimiter();

        builder.AddComposite("composite-policy", l1, l2);

        var registry = builder.Build();
        var limiter = registry.GetPolicy("composite-policy");

        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<CompositeRateLimiter>();
    }

    [Fact]
    public async Task Builder_AddFixedWindow_RegistersPolicyAndAppliesCustomOptions()
    {
        var configured = false;
        var builder = new RateLimiterPolicyBuilder();
        builder.AddFixedWindow("fixed-policy", opts =>
        {
            opts.PermitLimit = 1;
            configured = true;
        });

        var registry = builder.Build();
        var limiter = registry.GetPolicy("fixed-policy");

        configured.Should().BeTrue();
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<FixedWindowRateLimiter>();

        var lease1 = await limiter!.AcquireAsync("tenant-1", 1);
        lease1.Value.IsAcquired.Should().BeTrue();

        var lease2 = await limiter.AcquireAsync("tenant-1", 1);
        lease2.Value.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task Builder_AddSlidingWindow_RegistersPolicyAndAppliesCustomOptions()
    {
        var configured = false;
        var builder = new RateLimiterPolicyBuilder();
        builder.AddSlidingWindow("sliding-policy", opts =>
        {
            opts.PermitLimit = 1;
            configured = true;
        });

        var registry = builder.Build();
        var limiter = registry.GetPolicy("sliding-policy");

        configured.Should().BeTrue();
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<SlidingWindowRateLimiter>();

        var lease1 = await limiter!.AcquireAsync("tenant-1", 1);
        lease1.Value.IsAcquired.Should().BeTrue();

        var lease2 = await limiter.AcquireAsync("tenant-1", 1);
        lease2.Value.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task Builder_AddTokenBucket_RegistersPolicyAndAppliesCustomOptions()
    {
        var configured = false;
        var builder = new RateLimiterPolicyBuilder();
        builder.AddTokenBucket("bucket-policy", opts =>
        {
            opts.PermitLimit = 1;
            configured = true;
        });

        var registry = builder.Build();
        var limiter = registry.GetPolicy("bucket-policy");

        configured.Should().BeTrue();
        limiter.Should().NotBeNull();
        limiter.Should().BeOfType<TokenBucketRateLimiter>();

        var lease1 = await limiter!.AcquireAsync("tenant-1", 1);
        lease1.Value.IsAcquired.Should().BeTrue();

        var lease2 = await limiter.AcquireAsync("tenant-1", 1);
        lease2.Value.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void Builder_SetDefaultPolicy_ByName_Existing_SetsCorrectly()
    {
        var builder = new RateLimiterPolicyBuilder();
        builder.AddFixedWindow("main-policy", opts => opts.PermitLimit = 10);
        builder.SetDefaultPolicy("main-policy");

        var registry = builder.Build();
        registry.DefaultLimiter.Should().NotBeNull();
        registry.DefaultLimiter.Should().BeSameAs(registry.GetPolicy("main-policy"));
    }

    [Fact]
    public void Builder_AddPolicy_RegistersPreInstantiatedPolicy()
    {
        var builder = new RateLimiterPolicyBuilder();
        var customLimiter = new TokenBucketRateLimiter();

        builder.AddPolicy("custom-token", customLimiter);

        var registry = builder.Build();
        registry.GetPolicy("custom-token").Should().BeSameAs(customLimiter);
    }

    [Fact]
    public void Builder_SetDefaultPolicy_ByInstance_SetsCorrectly()
    {
        var builder = new RateLimiterPolicyBuilder();
        var defaultLimiter = new FixedWindowRateLimiter();

        builder.SetDefaultPolicy(defaultLimiter);

        var registry = builder.Build();
        registry.DefaultLimiter.Should().BeSameAs(defaultLimiter);
    }

    [Fact]
    public void Builder_SetDefaultPolicy_ByInstance_Null_ThrowsArgumentNullException()
    {
        var builder = new RateLimiterPolicyBuilder();

        var act = () => builder.SetDefaultPolicy((IRateLimiter)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Builder_SetDefaultPolicy_ByName_Unregistered_ThrowsInvalidOperationException()
    {
        var builder = new RateLimiterPolicyBuilder();

        var act = () => builder.SetDefaultPolicy("nonexistent");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*nonexistent*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Builder_SetDefaultPolicy_ByName_InvalidString_ThrowsArgumentException(string? invalidName)
    {
        var builder = new RateLimiterPolicyBuilder();

        var act = () => builder.SetDefaultPolicy(invalidName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Builder_GuardClauses_ThrowExpectedExceptions()
    {
        var builder = new RateLimiterPolicyBuilder();

        // FixedWindow
        Assert.ThrowsAny<ArgumentException>(() => builder.AddFixedWindow(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => builder.AddFixedWindow("p", null!));

        // SlidingWindow
        Assert.ThrowsAny<ArgumentException>(() => builder.AddSlidingWindow("", _ => { }));
        Assert.Throws<ArgumentNullException>(() => builder.AddSlidingWindow("p", null!));

        // TokenBucket
        Assert.ThrowsAny<ArgumentException>(() => builder.AddTokenBucket(" ", _ => { }));
        Assert.Throws<ArgumentNullException>(() => builder.AddTokenBucket("p", null!));

        // Concurrency
        Assert.ThrowsAny<ArgumentException>(() => builder.AddConcurrency(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => builder.AddConcurrency("p", null!));

        // Composite
        Assert.ThrowsAny<ArgumentException>(() => builder.AddComposite(null!, new FixedWindowRateLimiter()));
        Assert.Throws<ArgumentNullException>(() => builder.AddComposite("p", (IRateLimiter[])null!));

        // AddPolicy
        Assert.ThrowsAny<ArgumentException>(() => builder.AddPolicy("", new FixedWindowRateLimiter()));
        Assert.Throws<ArgumentNullException>(() => builder.AddPolicy("p", null!));
    }
}
