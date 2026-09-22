// Copyright © Erickson Lopez. MIT License.
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Provides OpenTelemetry-compatible metrics instrumentation for the rate limiting ecosystem.
/// </summary>
public static class RateLimitingMetrics
{
    /// <summary>
    /// Specifies the name of the meter instrumenting rate limiting operations.
    /// </summary>
    public const string MeterName = "EricksonLopez.RateLimiting";

    /// <summary>
    /// Specifies the version string of the meter, synchronized with the package version.
    /// </summary>
    public const string MeterVersion = "1.0.0";

    internal static readonly Meter Meter = new(MeterName, MeterVersion);

    /// <summary>
    /// Encapsulates the counter tracking total rate limit evaluation attempts.
    /// </summary>
    public static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "rate_limit.requests.total",
        unit: "{request}",
        description: "Total number of rate limit permit evaluation attempts.");

    /// <summary>
    /// Encapsulates the histogram tracking latency of rate limit permit acquisition attempts in milliseconds.
    /// </summary>
    public static readonly Histogram<double> LeaseDuration = Meter.CreateHistogram<double>(
        "rate_limit.lease.duration",
        unit: "ms",
        description: "Duration of rate limit permit acquisition attempt.");

    /// <summary>
    /// Records a rate limiter permit evaluation attempt and its duration.
    /// </summary>
    /// <param name="limiterType">The algorithm or classification of the rate limiter</param>
    /// <param name="status">The outcome status of the evaluation, such as acquired, rejected, or failed</param>
    /// <param name="durationMs">The elapsed duration of the acquisition attempt in milliseconds</param>
    public static void RecordRequest(string limiterType, string status, double durationMs)
    {
        var tags = new TagList
        {
            { "limiter.type", limiterType },
            { "status", status }
        };

        RequestsTotal.Add(1, in tags);
        LeaseDuration.Record(durationMs, in tags);
    }
}
