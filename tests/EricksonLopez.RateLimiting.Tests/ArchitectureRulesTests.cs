// Copyright © Erickson Lopez. MIT License.
using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class ArchitectureRulesTests
{
    [Fact]
    public void Core_Classes_ShouldBeSealed()
    {
        var result = Types.InAssembly(typeof(IRateLimiter).Assembly)
            .That()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .AreNotStatic()
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Core_ShouldNotDependOn_AspNetCore()
    {
        var result = Types.InAssembly(typeof(IRateLimiter).Assembly)
            .ShouldNot()
            .HaveDependencyOn("EricksonLopez.RateLimiting.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Core_ShouldNotDependOn_Redis()
    {
        var result = Types.InAssembly(typeof(IRateLimiter).Assembly)
            .ShouldNot()
            .HaveDependencyOn("EricksonLopez.RateLimiting.Redis")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Core_Interfaces_ShouldStartWithI()
    {
        var result = Types.InAssembly(typeof(IRateLimiter).Assembly)
            .That()
            .AreInterfaces()
            .Should()
            .HaveNameStartingWith("I")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
