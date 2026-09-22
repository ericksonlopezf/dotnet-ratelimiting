# EricksonLopez.RateLimiting — Official Reference Showcase

[![.NET Multi-Targeting](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native%20AOT-100%25%20Compatible-brightgreen)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](../../LICENSE)

Welcome to the **Official Reference Implementation** and **Executable Documentation** for the `EricksonLopez.RateLimiting` library suite.

This Showcase project demonstrates exhaustive, compilable, and production-grade usage of the complete public API of the rate limiting engine, its ASP.NET Core integration, and its distributed Redis extensions.

---

## 📑 Table of Contents

1. [Overview](#-overview)
2. [Project Structure and Pedagogical Levels](#-project-structure-and-pedagogical-levels)
3. [Technical Documentation Index](#-technical-documentation-index)
4. [Quick Showcase Execution](#-quick-showcase-execution)
5. [Summary of Available Algorithms](#-summary-of-available-algorithms)
6. [Compatibility and Support Matrix](#-compatibility-and-support-matrix)

---

## 🚀 Overview

`EricksonLopez.RateLimiting` provides an ultra-high-throughput rate limiting engine for .NET, characterized by:
- **Zero-Allocation**: `RateLimitLease` implemented as a `readonly record struct` to eliminate Garbage Collector pressure.
- **100% Native AOT Compatible**: Zero reflection invocations, zero dynamic serializers, ready for native compilation in ultra-lightweight containers.
- **Built-in Algorithms**: Fixed Window, segmented Sliding Window, continuous Token Bucket, Concurrency limiter, and Composite limiter with automatic transactional rollback.
- **Distributed Redis Coordination**: Atomic Lua scripts over Redis Sorted Sets (ZSET) and Hashes to coordinate quotas across multi-pod cluster replicas.
- **Operational Resilience**: Fail-Open (high business availability) and Fail-Closed (strict security) modes with structured diagnostic callbacks.

---

## 🎓 Project Structure and Pedagogical Levels

The Showcase is structured across 11 progressive pedagogical levels (Levels 0 through 10):

| Level | Title | Description |
|---|---|---|
| **Level 0** | Conceptual Foundation | Theoretical background, problems solved, trade-offs, and comparison with `System.Threading.RateLimiting`. |
| **Level 1** | Quick Start | Installation, minimal in-memory configuration, and dependency injection registration. |
| **Level 2** | Complete Configuration | Comprehensive parameterization of window options, segments, maximum partition counts, and middleware settings. |
| **Level 3** | Real-World Use Cases | Partitioning by remote IP, authenticated user Claims, and multi-tenant SaaS architectures. |
| **Level 4** | Advanced Integration | Fluent `RateLimiterPolicyBuilder`, named policy registry, and Minimal API endpoint conventions. |
| **Level 5** | Concurrency Throttling | In-flight execution throttling (`ConcurrencyRateLimiter`) with safe deterministic disposal via `using var lease`. |
| **Level 6** | Error Handling & Resilience | RFC 7807 (ProblemDetails) responses, Fail-Open behavior during Redis outages, and public error codes. |
| **Level 7** | Scalability & Observability | Native OpenTelemetry instrumentation with `RateLimitingMetrics` (`Meter`, `Counter`, `Histogram`). |
| **Level 8** | Customization & Extensibility | Developing custom limiters implementing the `IRateLimiter` contract. |
| **Level 9** | Distributed Extensions | Redis distributed limiters (`RedisSlidingWindowRateLimiter` and `RedisTokenBucketRateLimiter`). |
| **Level 10** | Enterprise Architecture | Multi-tier composite policies (`CompositeRateLimiter`: burst + sustained + concurrency). |

---

## 📚 Technical Documentation Index

Detailed technical manuals are located in [`docs/`](./docs/):

- [**Phase 0 · Repository Discovery**](./docs/00-repository-discovery.md): Formal taxonomy and project classification.
- [**Phase 1 · Public API Inventory**](./docs/01-public-api-inventory.md): Complete catalog of classes, interfaces, methods, and overloads.
- [**Phase 2 · Functional Map & Request Flows**](./docs/02-functional-map.md): Request lifecycle and layer transitions.
- [**Phase 3 · Detailed Progressive Showcase**](./docs/03-progressive-showcase.md): In-depth level-by-level walkthrough with code snippets.
- [**Phase 4 · Official Cookbook Recipes**](./docs/04-cookbook.md): Production recipes for brute-force protection, fail-open, SaaS, and multi-interval quotas.
- [**Phase 5 · Technical API Reference**](./docs/05-api-reference.md): Microsoft Learn style specifications for every public method.
- [**Phase 7 · Architecture Diagrams (Mermaid)**](./docs/06-architecture-and-diagrams.md): Component, sequence, and state machine diagrams.
- [**Phase 8 · Operational & Troubleshooting Guides**](./docs/07-guides-and-troubleshooting.md): Quick start, FAQ, troubleshooting, and migration guide.
- [**Phases 9 & 10 · Audit & Final Validation**](./docs/08-synchronization-and-validation.md): Reconciliation matrix and 15-point quality certification.

---

## ⚡ Quick Showcase Execution

To build and run the complete demonstration suite:

```bash
# From repository root:
dotnet run --project samples/Showcase/EricksonLopez.RateLimiting.Showcase.csproj
```

---

## 🛠 Summary of Available Algorithms

### 1. Fixed Window (`FixedWindowRateLimiter`)
Divides time into discrete intervals (e.g., 1 minute). When the counter reaches the limit, further requests are rejected until the start of the next interval. Lowest CPU overhead.

### 2. Sliding Window (`SlidingWindowRateLimiter`)
Divides the time window into uniform sub-segments (e.g., 6 segments of 10s for 1 minute). Calculates weighted quotas based on elapsed time, eliminating boundary burst spikes.

### 3. Token Bucket (`TokenBucketRateLimiter`)
A bucket accumulates tokens at a constant rate up to its maximum capacity. Allows instantaneous bursts if tokens are available and refills continuously based on elapsed time.

### 4. Concurrency Limiter (`ConcurrencyRateLimiter`)
Restricts concurrent parallel executions. A permit is acquired when starting a task and released atomically when the lease is disposed (`using var lease`).

### 5. Composite Limiter (`CompositeRateLimiter`)
Evaluates multiple algorithms sequentially using AND logic. If any limiter rejects, it immediately rolls back permits granted by previous limiters.

---

## 📊 Compatibility and Support Matrix

| Platform | Support | Notes |
|---|---|---|
| **.NET 8.0** | ✅ Supported | Long Term Support (LTS) |
| **.NET 9.0** | ✅ Supported | Standard Term Support (STS) |
| **.NET 10.0** | ✅ Supported | Active development version of the Showcase |
| **Native AOT** | ✅ Verified | 0 trimming warnings |
| **Redis** | ✅ Supported | Requires Redis 6.0+ (Lua scripts with ZREMRANGEBYSCORE and HSET) |
