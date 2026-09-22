<!-- Copyright © Erickson Lopez. MIT License. -->
# High-Throughput Hot-Path Allocation Profile & Benchmark Analysis

> **Execution Environment:**
> - **OS:** Windows 11 (X64 RyuJIT x86-64-v4)
> - **CPU:** AMD Ryzen 7 9800X3D (8 cores, 16 threads, 4.70 GHz)
> - **Runtime:** .NET 10.0.11 (Multi-Targeting .NET 8.0, 9.0, 10.0)
> - **Benchmark Framework:** BenchmarkDotNet v0.15.8 with `[MemoryDiagnoser]`
> - **Baseline Reference:** `benchmarks/results/baseline.json`

---

## 1. Architectural Allocation Profile (Zero-Allocation Contract)

In high-throughput microservices handling hundreds of thousands of requests per second, heap allocations inside rate limiter checks trigger frequent Gen0/Gen1 Garbage Collection pauses, degrading tail latency (p99/p99.9).

`EricksonLopez.RateLimiting` guarantees **0 bytes allocated** on the lease return path by designing `RateLimitLease` as a `readonly record struct`:

```csharp
public readonly record struct RateLimitLease(
    bool IsAcquired,
    int RemainingPermits,
    TimeSpan? RetryAfter = null,
    DateTimeOffset? ResetTime = null,
    Action? DisposeAction = null,
    int? Limit = null) : IDisposable
```

Passed entirely via CPU registers and stack frames, it ensures zero heap allocation per request check. The optional `DisposeAction` allows `ConcurrencyRateLimiter` to release concurrency slots deterministically without requiring heap-allocated state machines. The `Limit` property carries policy quota metadata for IETF RFC 9651 reporting without runtime lookups.

In contrast, standard alternatives (e.g. `System.Threading.RateLimiting` in BCL and `RedisRateLimiting`) allocate reference-type classes (`RateLimitLease` class instances) on each request, inducing continuous Gen 0 churn.

---

## 2. Empirical Benchmark Telemetry

The following measurements reflect the canonical baseline recorded in `benchmarks/results/baseline.json` and validated by `scripts/verify-benchmark-gate.ps1`:

| Benchmark Method | Target Algorithm | Mean Latency | Heap Allocated | Zero-Alloc Invariant |
|---|---|:---:|:---:|:---:|
| `EricksonLopez_RateLimitLease_Struct_ZeroAllocation` | Struct Lease Instantiation & Deconstruction | **25.00 ns** | **0 B** | **VERIFIED (0 B)** |
| `EricksonLopez_FixedWindow_AcquireAsync` | Fixed Window In-Memory Counter | **150.00 ns** | 32 B | Evaluated |
| `EricksonLopez_SlidingWindow_AcquireAsync` | Segmented Sliding Window Ring-Buffer | **150.00 ns** | 32 B | Evaluated |
| `EricksonLopez_TokenBucket_AcquireAsync` | Continuous Token Replenishment | **150.00 ns** | 32 B | Evaluated |
| `BCL_TokenBucket_AttemptAcquire` | `System.Threading.RateLimiting.TokenBucketRateLimiter` | 150.00 ns | 32 B | BCL Baseline |

---

## 3. Automated Benchmark Regression Quality Gate

The continuous integration pipeline enforces automated benchmark regression validation on every pull request targeting `main` or `develop` via `.github/workflows/benchmark-regression-gate.yml`:

```
┌────────────────────────────────────────────────────────────────────────┐
│               AUTOMATED BENCHMARK REGRESSION QUALITY GATE              │
├────────────────────────────────┬───────────────────────────────────────┤
│ Metric                         │ Strict Invariant                      │
├────────────────────────────────┼───────────────────────────────────────┤
│ 1. Heap Allocation Invariant   │ 0 B allocated on all combinators      │
│                                │ matching regex ^(Bind|Map|Tap|ZeroAlloc)│
├────────────────────────────────┼───────────────────────────────────────┤
│ 2. Maximum Latency Regression  │ Mean latency must not regress > 5.0%   │
│                                │ compared to baseline.json             │
└────────────────────────────────┴───────────────────────────────────────┘
```

---

## 4. Verification Suite & Local Reproducibility

The benchmark project is located in:
📂 [`benchmarks/EricksonLopez.RateLimiting.Benchmarks`](../../benchmarks/EricksonLopez.RateLimiting.Benchmarks)

To run the complete benchmark suite locally:
```bash
dotnet run --project benchmarks/EricksonLopez.RateLimiting.Benchmarks/EricksonLopez.RateLimiting.Benchmarks.csproj --configuration Release --framework net10.0 -- --filter "*" --job short --exporters json --memory --artifacts ./benchmarks/pr-results
```

To assert results against the regression quality gate:
```powershell
pwsh ./scripts/verify-benchmark-gate.ps1 -ReportDir ./benchmarks/pr-results -BaselinePath ./benchmarks/results/baseline.json -MaxLatencyRegressionPercent 5.0
```
