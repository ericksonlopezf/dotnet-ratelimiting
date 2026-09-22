// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.RateLimiting.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.RateLimiting.AspNetCore.Tests;

public sealed class NamedPoliciesTests
{
    private readonly IRateLimiter _defaultLimiter = Substitute.For<IRateLimiter>();
    private readonly IRateLimiter _customLimiter = Substitute.For<IRateLimiter>();
    private readonly RateLimiterPolicyRegistry _registry = new();
    private readonly RateLimitingMiddlewareOptions _options = new()
    {
        PartitionKeyResolver = _ => "test-user"
    };

    public NamedPoliciesTests()
    {
        _registry.DefaultLimiter = _defaultLimiter;
        _registry.Register("custom-policy", _customLimiter);
    }

    [Fact]
    public async Task InvokeAsync_EndpointWithEnableRateLimiting_UsesNamedPolicy()
    {
        _customLimiter.AcquireAsync("test-user", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(10));

        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(_options));
        var context = new DefaultHttpContext();

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute("custom-policy")),
            "CustomEndpoint");
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        await middleware.InvokeAsync(context, rateLimiter: null, policyRegistry: _registry);

        await _customLimiter.Received(1).AcquireAsync("test-user", 1, Arg.Any<CancellationToken>());
        await _defaultLimiter.DidNotReceive().AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_EndpointWithDisableRateLimiting_BypassesLimiting()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RateLimitingMiddleware(next, Options.Create(_options));
        var context = new DefaultHttpContext();

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new DisableRateLimitingAttribute()),
            "ExemptEndpoint");
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        await middleware.InvokeAsync(context, rateLimiter: null, policyRegistry: _registry);

        nextCalled.Should().BeTrue();
        await _defaultLimiter.DidNotReceive().AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _customLimiter.DidNotReceive().AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_EndpointWithoutMetadata_UsesDefaultLimiter()
    {
        _defaultLimiter.AcquireAsync("test-user", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(5));

        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(_options));
        var context = new DefaultHttpContext();

        var endpoint = new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "DefaultEndpoint");
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        await middleware.InvokeAsync(context, rateLimiter: null, policyRegistry: _registry);

        await _defaultLimiter.Received(1).AcquireAsync("test-user", 1, Arg.Any<CancellationToken>());
        await _customLimiter.DidNotReceive().AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_NonexistentPolicy_ThrowsInvalidOperationException()
    {
        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(_options));
        var context = new DefaultHttpContext();

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute("non-existent-policy")),
            "MissingEndpoint");
        context.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        Func<Task> act = () => middleware.InvokeAsync(context, rateLimiter: null, policyRegistry: _registry);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-existent-policy*");
    }

    [Fact]
    public void RateLimiterPolicyBuilder_BuildsRegistryCorrectly()
    {
        var builder = new RateLimiterPolicyBuilder();
        builder.AddFixedWindow("fixed-1", opts => { opts.PermitLimit = 10; })
               .AddSlidingWindow("sliding-1", opts => { opts.PermitLimit = 20; })
               .AddTokenBucket("bucket-1", opts => { opts.PermitLimit = 30; })
               .SetDefaultPolicy("fixed-1");

        var registry = builder.Build();

        registry.GetPolicy("fixed-1").Should().NotBeNull();
        registry.GetPolicy("fixed-1").Should().BeOfType<FixedWindowRateLimiter>();
        registry.GetPolicy("sliding-1").Should().NotBeNull();
        registry.GetPolicy("sliding-1").Should().BeOfType<SlidingWindowRateLimiter>();
        registry.GetPolicy("bucket-1").Should().NotBeNull();
        registry.GetPolicy("bucket-1").Should().BeOfType<TokenBucketRateLimiter>();
        registry.DefaultLimiter.Should().BeSameAs(registry.GetPolicy("fixed-1"));
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }
}
