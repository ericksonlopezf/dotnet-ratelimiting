// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.RateLimiting.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.RateLimiting.AspNetCore.Tests;

public sealed class MegaAuditAspNetCoreAdversarialSuite
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void RateLimitingMiddlewareOptions_PermitCost_MustRejectNonPositiveValues(int invalidCost)
    {
        var options = new RateLimitingMiddlewareOptions();
        Action act = () => options.PermitCost = invalidCost;
        act.Should().Throw<ArgumentOutOfRangeException>(
            "PermitCost <= 0 violates rate limiting invariants and causes runtime unhandled exceptions");
    }

    [Fact]
    public async Task RateLimitingMiddleware_ResponseAlreadyStarted_MustNotThrowOnHeaderSet()
    {
        RequestDelegate next = async ctx =>
        {
            // Simulate response already started (e.g. streaming, chunked, SSE)
            await ctx.Response.Body.WriteAsync(new byte[] { 1, 2, 3 });
        };

        var options = new RateLimitingMiddlewareOptions { PermitCost = 1 };
        var limiter = Substitute.For<IRateLimiter>();
        limiter.AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(RateLimitLease.Successful(remainingPermits: 10, resetTime: null, limit: 10));

        var middleware = new RateLimitingMiddleware(next, Options.Create(options));
        var context = new DefaultHttpContext();

        // Must complete without unhandled exception
        Func<Task> act = () => middleware.InvokeAsync(context, limiter);
        await act.Should().NotThrowAsync();
    }
}
