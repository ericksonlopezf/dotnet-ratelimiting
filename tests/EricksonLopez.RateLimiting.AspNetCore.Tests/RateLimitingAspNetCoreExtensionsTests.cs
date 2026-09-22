// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.RateLimiting.Policies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.RateLimiting.AspNetCore.Tests;

public sealed class RateLimitingAspNetCoreExtensionsTests
{
    [Fact]
    public void AddHttpRateLimiting_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        services.AddHttpRateLimiting(opts =>
        {
            opts.PermitCost = 5;
            opts.FailClosed = true;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RateLimitingMiddlewareOptions>>().Value;

        options.PermitCost.Should().Be(5);
        options.FailClosed.Should().BeTrue();
    }

    [Fact]
    public void AddHttpRateLimiting_NullConfigure_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddHttpRateLimiting();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddRateLimiting_RegistersRegistryAndDefaultLimiter()
    {
        var services = new ServiceCollection();
        services.AddRateLimiting(builder =>
        {
            builder.AddFixedWindow("fixed-tier", opts => opts.PermitLimit = 10)
                   .SetDefaultPolicy("fixed-tier");
        }, middlewareOpts =>
        {
            middlewareOpts.PermitCost = 2;
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetService<IRateLimiterPolicyRegistry>();
        var defaultLimiter = provider.GetService<IRateLimiter>();
        var middlewareOptions = provider.GetRequiredService<IOptions<RateLimitingMiddlewareOptions>>().Value;

        registry.Should().NotBeNull();
        defaultLimiter.Should().NotBeNull();
        defaultLimiter.Should().BeSameAs(registry!.DefaultLimiter);
        middlewareOptions.PermitCost.Should().Be(2);
    }

    [Fact]
    public void AddRateLimiting_WithoutDefaultLimiter_RegistersOnlyRegistry()
    {
        var services = new ServiceCollection();
        services.AddRateLimiting(builder =>
        {
            builder.AddFixedWindow("tier", opts => opts.PermitLimit = 10);
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetService<IRateLimiterPolicyRegistry>();
        var defaultLimiter = provider.GetService<IRateLimiter>();

        registry.Should().NotBeNull();
        defaultLimiter.Should().BeNull();
    }

    [Fact]
    public void UseHttpRateLimiting_AddsMiddlewareToPipeline()
    {
        var app = Substitute.For<IApplicationBuilder>();

        app.UseHttpRateLimiting();

        app.Received(1).Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>());
    }

    [Fact]
    public void GuardClauses_ThrowArgumentNullException()
    {
        IServiceCollection nullServices = null!;
        IApplicationBuilder nullApp = null!;

        Assert.Throws<ArgumentNullException>(() => nullServices.AddHttpRateLimiting());
        Assert.Throws<ArgumentNullException>(() => nullServices.AddRateLimiting(_ => { }));

        var validServices = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => validServices.AddRateLimiting(null!));

        Assert.Throws<ArgumentNullException>(() => nullApp.UseHttpRateLimiting());
    }
}
