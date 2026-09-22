# ADR-005: OpenTelemetry Metrics and Observability

## Status
Accepted

## Date
2026-09-04

## Context
High-throughput rate limiters operating at hundreds of thousands of requests per second cannot rely on verbose text logging to communicate runtime operational state. Platform engineering teams running Kubernetes clusters require real-time structured telemetry:
- How many requests are being acquired vs. rejected per policy?
- What is the latency distribution of rate limit checks (especially across distributed Redis network calls)?
- Are Redis timeouts or degraded fallbacks occurring?

Furthermore, instrumentation must NOT violate our non-negotiable performance invariants:
- Must NOT cause heap allocations on the hot path.
- Must NOT use runtime reflection or dynamic code generation that breaks Native AOT.
- Must NOT force third-party OpenTelemetry SDK dependencies onto core library consumers.

## Decision
We integrate native OpenTelemetry metrics using the .NET BCL `System.Diagnostics.Metrics` APIs directly within `EricksonLopez.RateLimiting`.

1. **Meter Specification**:
   - Meter Name: `"EricksonLopez.RateLimiting"`
   - Meter Version: `"1.0.0"`
   - Exposed statically via `RateLimitingMetrics.MeterName` for zero-friction registration in OpenTelemetry builder pipelines (`.AddMeter(RateLimitingMetrics.MeterName)`).

2. **Standard Instruments**:
   - **`rate_limit.requests.total`** (`Counter<long>`):
     - Unit: `"{request}"`
     - Description: Total number of permit evaluation attempts.
     - Dimension Tags:
       - `limiter.type`: `"sliding_window"`, `"token_bucket"`, `"fixed_window"`, `"concurrency"`, `"composite"`, `"redis_sliding_window"`, `"redis_token_bucket"`
       - `status`: `"acquired"`, `"rejected"`, `"failed"`
   - **`rate_limit.lease.duration`** (`Histogram<double>`):
     - Unit: `"ms"`
     - Description: Latency of permit acquisition attempts in milliseconds.
     - Dimension Tags:
       - `limiter.type`
       - `status`

3. **Zero-Allocation Recording via `TagList`**:
   - Measurements are recorded using `TagList` structs passed by `in` reference:
     ```csharp
     var tags = new TagList
     {
         { "limiter.type", limiterType },
         { "status", status }
     };
     RequestsTotal.Add(1, in tags);
     LeaseDuration.Record(durationMs, in tags);
     ```
   - `TagList` stores up to 8 tag key-value pairs inline without heap allocations.
   - Durations are calculated using `Stopwatch.GetTimestamp()` and `Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds`, avoiding `DateTimeOffset` arithmetic allocations.

4. **Zero-Overhead When Unobserved**:
   - If no `MeterListener` or OpenTelemetry exporter is listening to `"EricksonLopez.RateLimiting"`, metric emission checks are short-circuited by the BCL runtime at near-zero CPU cost.

## Consequences

### Positive
- **Out-of-the-Box Observability**: Dashboards in Grafana, Datadog, Prometheus, and Azure Monitor can immediately visualize rate limit throughput and rejection rates.
- **Zero Allocations & AOT Verified**: Retains 100% Native AOT compliance and produces 0 GC allocations for telemetry.
- **No Third-Party Package Bloat**: Uses native BCL runtime primitives (`System.Diagnostics.Metrics`) without requiring external OpenTelemetry packages in core libraries.

### Negative
- Applications running on platforms that do not support `System.Diagnostics.Metrics` cannot consume these metrics without adapter bridges.

## References
- [ADR-001: Distributed Rate Limiting](./adr-001-distributed-rate-limiting.md)
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
- [ADR-004: Resilient Degradation & Middleware Callbacks](./adr-004-resilient-degradation-and-middleware-callbacks.md)
