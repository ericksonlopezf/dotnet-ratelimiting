// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

[Collection("MetricsCollection")]
public sealed class MetricsTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string MetricName, long Value, Dictionary<string, object?> Tags)> _counterMeasurements = [];
    private readonly List<(string MetricName, double Value, Dictionary<string, object?> Tags)> _histogramMeasurements = [];
    private readonly object _lock = new();

    public MetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == RateLimitingMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                dict[tag.Key] = tag.Value;
            }
            lock (_lock)
            {
                _counterMeasurements.Add((instrument.Name, measurement, dict));
            }
        });

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                dict[tag.Key] = tag.Value;
            }
            lock (_lock)
            {
                _histogramMeasurements.Add((instrument.Name, measurement, dict));
            }
        });

        _listener.Start();
    }

    [Fact]
    public async Task AcquireAsync_RecordsMetricsOnSuccessAndRejection()
    {
        var limiter = new FixedWindowRateLimiter(new RateLimiterOptions { PermitLimit = 1 });

        await limiter.AcquireAsync("test-metrics-key", 1);
        await limiter.AcquireAsync("test-metrics-key", 1);

        _listener.RecordObservableInstruments();

        lock (_lock)
        {
            _counterMeasurements.Should().Contain(m =>
                m.MetricName == "rate_limit.requests.total" &&
                object.Equals(m.Tags["limiter.type"], "fixed_window") &&
                object.Equals(m.Tags["status"], "acquired"));

            _counterMeasurements.Should().Contain(m =>
                m.MetricName == "rate_limit.requests.total" &&
                object.Equals(m.Tags["limiter.type"], "fixed_window") &&
                object.Equals(m.Tags["status"], "rejected"));

            _histogramMeasurements.Should().Contain(m =>
                m.MetricName == "rate_limit.lease.duration" &&
                object.Equals(m.Tags["limiter.type"], "fixed_window"));
        }
    }

    [Fact]
    public void RecordRequest_DirectCall_RecordsCounterAndHistogram()
    {
        RateLimitingMetrics.RecordRequest("custom_limiter", "failed", 42.5);

        _listener.RecordObservableInstruments();

        lock (_lock)
        {
            _counterMeasurements.Should().Contain(m =>
                m.MetricName == "rate_limit.requests.total" &&
                object.Equals(m.Tags["limiter.type"], "custom_limiter") &&
                object.Equals(m.Tags["status"], "failed"));

            _histogramMeasurements.Should().Contain(m =>
                m.MetricName == "rate_limit.lease.duration" &&
                m.Value == 42.5 &&
                object.Equals(m.Tags["limiter.type"], "custom_limiter") &&
                object.Equals(m.Tags["status"], "failed"));
        }
    }

    [Fact]
    public void Constants_AreCorrect()
    {
        RateLimitingMetrics.MeterName.Should().Be("EricksonLopez.RateLimiting");
        RateLimitingMetrics.MeterVersion.Should().Be("1.0.0");
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}

