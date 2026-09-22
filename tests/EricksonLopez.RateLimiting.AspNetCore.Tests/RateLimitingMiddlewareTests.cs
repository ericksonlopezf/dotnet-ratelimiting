// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.RateLimiting.AspNetCore.Tests;

public sealed class RateLimitingMiddlewareTests
{
    private readonly IRateLimiter _rateLimiter = Substitute.For<IRateLimiter>();
    private readonly RateLimitingMiddlewareOptions _options = new()
    {
        PartitionKeyResolver = _ => "test-client-123",
        PermitCost = 1
    };

    [Fact]
    public async Task InvokeAsync_PermittedLease_AppendsRemainingAndLimitHeaderAndCallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        _rateLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(remainingPermits: 4));

        var middleware = new RateLimitingMiddleware(next, Options.Create(_options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _rateLimiter);

        nextCalled.Should().BeTrue();
        context.Response.Headers[RateLimitingHeaders.Limit].ToString().Should().Be("1");
        context.Response.Headers[RateLimitingHeaders.Remaining].ToString().Should().Be("4");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_NullOrEmptyKey_FallsBackToAnonymous()
    {
        var options = new RateLimitingMiddlewareOptions
        {
            PartitionKeyResolver = _ => string.Empty,
            PermitCost = 1
        };

        _rateLimiter.AcquireAsync("anonymous", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(remainingPermits: 5));

        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _rateLimiter);

        await _rateLimiter.Received(1).AcquireAsync("anonymous", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_RejectedLease_Sets429AndRetryAfterWithoutCallingNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        _rateLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Rejected(retryAfter: TimeSpan.FromSeconds(15)));

        var middleware = new RateLimitingMiddleware(next, Options.Create(_options));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _rateLimiter);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.ContentType.Should().Be("application/json");
        context.Response.Headers[RateLimitingHeaders.Limit].ToString().Should().Be("1");
        context.Response.Headers[RateLimitingHeaders.RetryAfter].ToString().Should().Be("15");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = new StreamReader(context.Response.Body).ReadToEnd();
        body.Should().Be("{\"code\":\"RateLimitExceeded\",\"error\":\"Rate limit exceeded. Please retry after 15 seconds.\"}");
    }

    [Fact]
    public async Task InvokeAsync_RejectedLease_WithCustomOnRejected_ExecutesCallback()
    {
        var customCallbackInvoked = false;
        var options = new RateLimitingMiddlewareOptions
        {
            PartitionKeyResolver = _ => "custom-client",
            OnRejected = (ctx, lease, ct) =>
            {
                customCallbackInvoked = true;
                ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return ctx.Response.WriteAsync("{\"custom\":\"rejected\"}", ct);
            }
        };

        _rateLimiter.AcquireAsync("custom-client", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Rejected(retryAfter: TimeSpan.FromSeconds(30)));

        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(options));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _rateLimiter);

        customCallbackInvoked.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = new StreamReader(context.Response.Body).ReadToEnd();
        body.Should().Be("{\"custom\":\"rejected\"}");
    }

    [Fact]
    public async Task InvokeAsync_Failure_FailOpenDefault_CallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        _rateLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(EricksonLopez.Result.Result<RateLimitLease>.Failure(
                EricksonLopez.Result.Error.Failure("Redis.Down", "Redis unavailable")));

        var middleware = new RateLimitingMiddleware(next, Options.Create(_options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _rateLimiter);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Failure_FailClosed_Returns503()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = new RateLimitingMiddlewareOptions
        {
            PartitionKeyResolver = _ => "test-client-123",
            FailClosed = true
        };

        _rateLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(EricksonLopez.Result.Result<RateLimitLease>.Failure(
                EricksonLopez.Result.Error.Failure("Redis.Down", "Redis unavailable")));

        var middleware = new RateLimitingMiddleware(next, Options.Create(options));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _rateLimiter);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.ContentType.Should().Be("application/json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = new StreamReader(context.Response.Body).ReadToEnd();
        body.Should().Be("{\"code\":\"Redis.Down\",\"error\":\"Redis unavailable\"}");
    }

    [Fact]
    public async Task InvokeAsync_Failure_WithOnRedisFailureCallback_InvokesCallback()
    {
        var failureCallbackInvoked = false;
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = new RateLimitingMiddlewareOptions
        {
            PartitionKeyResolver = _ => "test-client-123",
            OnRedisFailure = (ctx, err, ct) =>
            {
                failureCallbackInvoked = true;
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Task.CompletedTask;
            }
        };

        _rateLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(EricksonLopez.Result.Result<RateLimitLease>.Failure(
                EricksonLopez.Result.Error.Failure("Redis.Down", "Redis unavailable")));

        var middleware = new RateLimitingMiddleware(next, Options.Create(options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _rateLimiter);

        failureCallbackInvoked.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public void Constructor_Guards_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RateLimitingMiddleware(null!, Options.Create(_options)));
        Assert.Throws<ArgumentNullException>(() => new RateLimitingMiddleware(_ => Task.CompletedTask, null!));
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(_options));

        Func<Task> act = () => middleware.InvokeAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_NoLimiterAvailable_CallsNextDirectly()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RateLimitingMiddleware(next, Options.Create(_options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, rateLimiter: null, policyRegistry: null);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_LeaseWithResetTime_SetsXRateLimitResetHeader()
    {
        var resetTime = DateTimeOffset.UtcNow.AddSeconds(45);
        _rateLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(10, resetTime));

        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(_options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _rateLimiter);

        context.Response.Headers[RateLimitingHeaders.Reset].ToString()
            .Should().Be(resetTime.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task InvokeAsync_RejectedLeaseWithNullRetryAfter_SetsDefaultRetryAfterOneSecond()
    {
        var rejectedWithoutRetryAfter = new RateLimitLease(IsAcquired: false, RemainingPermits: 0, RetryAfter: null, ResetTime: null);
        _rateLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(rejectedWithoutRetryAfter);

        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(_options));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _rateLimiter);

        context.Response.Headers[RateLimitingHeaders.RetryAfter].ToString().Should().Be("1");
    }

    [Fact]
    public void DefaultPartitionKeyResolver_ResolvesRemoteIpOrAnonymous()
    {
        var options = new RateLimitingMiddlewareOptions();

        var contextWithIp = new DefaultHttpContext();
        contextWithIp.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100");
        options.PartitionKeyResolver(contextWithIp).Should().Be("192.168.1.100");

        var contextNoIp = new DefaultHttpContext();
        options.PartitionKeyResolver(contextNoIp).Should().Be("anonymous");
    }

    [Fact]
    public void RateLimitingHeaders_Constants_MatchExpected()
    {
        RateLimitingHeaders.Limit.Should().Be("X-RateLimit-Limit");
        RateLimitingHeaders.Remaining.Should().Be("X-RateLimit-Remaining");
        RateLimitingHeaders.Reset.Should().Be("X-RateLimit-Reset");
        RateLimitingHeaders.RetryAfter.Should().Be("Retry-After");
    }

    [Fact]
    public async Task InvokeAsync_EndpointWithoutDisableMetadata_AppliesRateLimiting()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        _rateLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(remainingPermits: 3));

        var middleware = new RateLimitingMiddleware(next, Options.Create(_options));
        var context = new DefaultHttpContext();
        var endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new object()), "TestEndpoint");
        context.SetEndpoint(endpoint);

        await middleware.InvokeAsync(context, _rateLimiter);

        nextCalled.Should().BeTrue();
        context.Response.Headers[RateLimitingHeaders.Remaining].ToString().Should().Be("3");
    }

    [Fact]
    public async Task InvokeAsync_RateLimiterResolution_FallbackToRequestServices()
    {
        var serviceLimiter = Substitute.For<IRateLimiter>();
        serviceLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(remainingPermits: 7));

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IRateLimiter)).Returns(serviceLimiter);

        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(_options));
        var context = new DefaultHttpContext
        {
            RequestServices = sp
        };

        await middleware.InvokeAsync(context, rateLimiter: null, policyRegistry: null);

        context.Response.Headers[RateLimitingHeaders.Remaining].ToString().Should().Be("7");
    }

    [Fact]
    public async Task InvokeAsync_RateLimiterResolution_PolicyRegistryDefaultTakesPrecedenceOverRequestServices()
    {
        var serviceLimiter = Substitute.For<IRateLimiter>();
        var registryLimiter = Substitute.For<IRateLimiter>();
        registryLimiter.AcquireAsync("test-client-123", 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(remainingPermits: 9));

        var registry = Substitute.For<EricksonLopez.RateLimiting.Policies.IRateLimiterPolicyRegistry>();
        registry.DefaultLimiter.Returns(registryLimiter);

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IRateLimiter)).Returns(serviceLimiter);

        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, Options.Create(_options));
        var context = new DefaultHttpContext
        {
            RequestServices = sp
        };

        await middleware.InvokeAsync(context, rateLimiter: null, policyRegistry: registry);

        context.Response.Headers[RateLimitingHeaders.Remaining].ToString().Should().Be("9");
        await serviceLimiter.DidNotReceive().AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}

