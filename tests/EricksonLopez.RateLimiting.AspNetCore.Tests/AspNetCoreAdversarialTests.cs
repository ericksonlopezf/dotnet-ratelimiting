// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.RateLimiting.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.RateLimiting.AspNetCore.Tests;

public sealed class AspNetCoreAdversarialTests
{
    [Fact]
    public async Task Verify_FailOpen_WithoutCallback_CallsNext()
    {
        // Fail-Open (FailClosed = false) without OnRedisFailure must allow traffic through downstream pipeline
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = new RateLimitingMiddlewareOptions
        {
            FailClosed = false
        };

        var limiter = Substitute.For<IRateLimiter>();
        limiter.AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EricksonLopez.Result.Result<RateLimitLease>.Failure(
                EricksonLopez.Result.Error.Failure("Redis.Timeout", "Connection timed out")));

        var middleware = new RateLimitingMiddleware(next, Options.Create(options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, limiter);

        nextCalled.Should().BeTrue("under Fail-Open without terminal callback, downstream pipeline must proceed");
    }

    [Fact]
    public async Task Verify_OnRedisFailure_WhenRegistered_ActsAsTerminalHandler()
    {
        // When OnRedisFailure callback is registered, it takes full ownership of response
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var hookCalled = false;
        var options = new RateLimitingMiddlewareOptions
        {
            FailClosed = false,
            OnRedisFailure = (ctx, err, ct) =>
            {
                hookCalled = true;
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Task.CompletedTask;
            }
        };

        var limiter = Substitute.For<IRateLimiter>();
        limiter.AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EricksonLopez.Result.Result<RateLimitLease>.Failure(
                EricksonLopez.Result.Error.Failure("Redis.Timeout", "Connection timed out")));

        var middleware = new RateLimitingMiddleware(next, Options.Create(options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, limiter);

        hookCalled.Should().BeTrue("operational failure hook must be invoked");
        nextCalled.Should().BeFalse("registered OnRedisFailure callback acts as terminal handler");
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Attack_RateLimitHeaders_LimitHeader_MustReportQuotaNotPermitCost()
    {
        // Vector: A rate limiter configured with quota of 100 permits evaluates a request with cost = 1.
        // The middleware writes X-RateLimit-Limit = options.PermitCost ("1") instead of quota ("100").
        // Clients receiving Limit: 1 and Remaining: 99 observe broken HTTP rate-limiting semantics!
        RequestDelegate next = _ => Task.CompletedTask;
        var options = new RateLimitingMiddlewareOptions
        {
            PermitCost = 1
        };

        var limiter = Substitute.For<IRateLimiter>();
        // Suppose quota is 100, remaining is 99
        limiter.AcquireAsync(Arg.Any<string>(), 1, Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(remainingPermits: 99, resetTime: null, limit: 100));

        var middleware = new RateLimitingMiddleware(next, Options.Create(options));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, limiter);

        var limitHeader = context.Response.Headers[RateLimitingHeaders.Limit].ToString();
        // LimitHeader cannot be "1" when Remaining is "99"! Remaining would be greater than limit!
        int.Parse(limitHeader, System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThanOrEqualTo(99,
            "X-RateLimit-Limit header must represent the rate limiting quota, not the single-request permit cost!");
    }

    [Fact]
    public void EndpointRateLimitingExtensions_GuardClauses_ThrowExpectedExceptions()
    {
        var dummyBuilder = Substitute.For<Microsoft.AspNetCore.Builder.IEndpointConventionBuilder>();

        Action actNullBuilder = () => EndpointRateLimitingExtensions.RequireRateLimiting<Microsoft.AspNetCore.Builder.IEndpointConventionBuilder>(null!, "policy");
        actNullBuilder.Should().Throw<ArgumentNullException>();

        Action actNullPolicy = () => dummyBuilder.RequireRateLimiting(null!);
        actNullPolicy.Should().Throw<ArgumentException>();

        Action actWhitespacePolicy = () => dummyBuilder.RequireRateLimiting("   ");
        actWhitespacePolicy.Should().Throw<ArgumentException>();

        Action actNullDisable = () => EndpointRateLimitingExtensions.DisableRateLimiting<Microsoft.AspNetCore.Builder.IEndpointConventionBuilder>(null!);
        actNullDisable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RateLimitingAspNetCoreExtensions_GuardClauses_AndRegistrations()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        Action actNullServices = () => RateLimitingAspNetCoreExtensions.AddRateLimiting(null!, _ => { });
        actNullServices.Should().Throw<ArgumentNullException>();

        Action actNullConfigure = () => services.AddRateLimiting(null!);
        actNullConfigure.Should().Throw<ArgumentNullException>();

        services.AddRateLimiting(b => b.AddConcurrency("test", opt => opt.PermitLimit = 5));
        var sp = services.BuildServiceProvider();

        sp.GetService(typeof(EricksonLopez.RateLimiting.Policies.IRateLimiterPolicyRegistry)).Should().NotBeNull();
    }

    [Fact]
    public void RateLimitingMiddlewareOptions_PermitCost_ValidationMessage()
    {
        var options = new RateLimitingMiddlewareOptions();
        options.PermitCost = 1;
        options.PermitCost.Should().Be(1);

        Action actZero = () => options.PermitCost = 0;
        actZero.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*PermitCost must be at least 1.*");
    }
}
