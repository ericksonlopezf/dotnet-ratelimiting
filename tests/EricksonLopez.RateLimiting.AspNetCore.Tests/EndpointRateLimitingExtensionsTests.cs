// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EricksonLopez.RateLimiting.AspNetCore.Tests;

public sealed class EndpointRateLimitingExtensionsTests
{
    private sealed class TestEndpointConventionBuilder : IEndpointConventionBuilder
    {
        public List<object> Metadata { get; } = [];

        public void Add(Action<EndpointBuilder> convention)
        {
            var endpointBuilder = new TestEndpointBuilder();
            convention(endpointBuilder);
            Metadata.AddRange(endpointBuilder.Metadata);
        }
    }

    private sealed class TestEndpointBuilder : EndpointBuilder
    {
        public override Endpoint Build() => new(RequestDelegate, new EndpointMetadataCollection(Metadata), DisplayName);
    }

    [Fact]
    public void RequireDistributedRateLimiting_AddsEnableRateLimitingAttribute()
    {
        var builder = new TestEndpointConventionBuilder();

        builder.RequireDistributedRateLimiting("custom-policy");

        builder.Metadata.Should().ContainSingle();
        var metadata = builder.Metadata[0].Should().BeOfType<EnableRateLimitingAttribute>().Subject;
        metadata.PolicyName.Should().Be("custom-policy");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireDistributedRateLimiting_InvalidPolicyName_ThrowsArgumentException(string? policyName)
    {
        var builder = new TestEndpointConventionBuilder();

        Action act = () => builder.RequireDistributedRateLimiting(policyName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequireDistributedRateLimiting_NullBuilder_ThrowsArgumentNullException()
    {
        IEndpointConventionBuilder builder = null!;

        Action act = () => builder.RequireDistributedRateLimiting("policy");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DisableDistributedRateLimiting_AddsDisableRateLimitingAttribute()
    {
        var builder = new TestEndpointConventionBuilder();

        builder.DisableDistributedRateLimiting();

        builder.Metadata.Should().ContainSingle();
        builder.Metadata[0].Should().BeOfType<DisableRateLimitingAttribute>();
    }

    [Fact]
    public void DisableDistributedRateLimiting_NullBuilder_ThrowsArgumentNullException()
    {
        IEndpointConventionBuilder builder = null!;

        Action act = () => builder.DisableDistributedRateLimiting();

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnableRateLimitingAttribute_WhenPolicyNameIsNullOrWhiteSpace_ThrowsArgumentException(string? policyName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new EnableRateLimitingAttribute(policyName!));
    }

    [Fact]
    public void EnableRateLimitingAttribute_WithValidPolicyName_SetsPolicyName()
    {
        var attr = new EnableRateLimitingAttribute("my-policy");
        attr.PolicyName.Should().Be("my-policy");
    }
}
