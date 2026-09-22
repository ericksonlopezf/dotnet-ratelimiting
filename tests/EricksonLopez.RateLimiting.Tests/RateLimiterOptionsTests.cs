// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

public sealed class RateLimiterOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new RateLimiterOptions();

        options.PermitLimit.Should().Be(100);
        options.Window.Should().Be(TimeSpan.FromMinutes(1));
        options.SegmentsPerWindow.Should().Be(6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(int.MaxValue)]
    public void PermitLimit_ValidValues_SetsCorrectly(int limit)
    {
        var options = new RateLimiterOptions { PermitLimit = limit };

        options.PermitLimit.Should().Be(limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void PermitLimit_LessThanOne_ThrowsArgumentOutOfRangeException(int limit)
    {
        var options = new RateLimiterOptions();

        Action act = () => options.PermitLimit = limit;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(86400)]
    public void Window_GreaterThanZero_SetsCorrectly(int seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        var options = new RateLimiterOptions { Window = span };

        options.Window.Should().Be(span);
    }

    [Fact]
    public void Window_ZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var options = new RateLimiterOptions();

        Action actZero = () => options.Window = TimeSpan.Zero;
        Action actNegative = () => options.Window = TimeSpan.FromMilliseconds(-1);

        actZero.Should().Throw<ArgumentOutOfRangeException>();
        actNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(60)]
    public void SegmentsPerWindow_ValidValues_SetsCorrectly(int segments)
    {
        var options = new RateLimiterOptions { SegmentsPerWindow = segments };

        options.SegmentsPerWindow.Should().Be(segments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void SegmentsPerWindow_LessThanOne_ThrowsArgumentOutOfRangeException(int segments)
    {
        var options = new RateLimiterOptions();

        Action act = () => options.SegmentsPerWindow = segments;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
