# DEPENDENCY GRAPH & EXTERNAL DEPENDENCIES AUDIT

**Audit Date:** 2026-09-05T04:46:00Z  
**Repository:** `ericksonlopezf/dotnet-ratelimiting`  
**Central Package Management:** Enabled (`Directory.Packages.props`)  

---

## 1. Architectural Layering & Dependency Flow

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│                      HTTP / APPLICATION CONSUMERS                           │
│           (ASP.NET Core Minimal APIs, Controllers, Endpoint Routing)        │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
                                       ▼
       ┌───────────────────────────────────────────────────────────────┐
       │             EricksonLopez.RateLimiting.AspNetCore             │
       │                   (Tier 1 HTTP Middleware)                    │
       │  • RateLimitingMiddleware      • EnableRateLimitingAttribute  │
       │  • RateLimitingHeaders         • EndpointRateLimitingExt      │
       └───────────────┬───────────────────────────────┬───────────────┘
                       │                               │
                       ▼                               ▼
┌────────────────────────────────────────┐ ┌──────────────────────────────────┐
│    EricksonLopez.RateLimiting.Redis    │ │   Microsoft.AspNetCore.App       │
│     (Tier 1 Distributed Engine)        │ │   (ASP.NET Core BCL Framework)   │
│  • RedisSlidingWindowRateLimiter       │ └──────────────────────────────────┘
│  • RedisTokenBucketRateLimiter         │
│  • StackExchange.Redis (2.8.47)        │
└──────────────────────┬─────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                       EricksonLopez.RateLimiting                            │
│                        (Tier 0 Core Abstraction)                            │
│  • IRateLimiter (Contract)             • RateLimitLease (Struct Token)      │
│  • FixedWindowRateLimiter              • SlidingWindowRateLimiter           │
│  • TokenBucketRateLimiter              • ConcurrencyRateLimiter             │
│  • CompositeRateLimiter                • RateLimiterPolicyBuilder           │
│  • RateLimitingMetrics (OTel)          • RateLimiterOptions                 │
└──────────────────────┬──────────────────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            FOUNDATIONAL TIER -1                             │
│  • EricksonLopez.Result (2.0.0)                                              │
│  • Microsoft.Extensions.Logging.Abstractions (10.0.11)                      │
│  • Microsoft.Extensions.DependencyInjection.Abstractions (10.0.11)          │
│  • System.Diagnostics.Metrics (BCL)                                         │
│  • System.Threading.TimeProvider (BCL)                                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Invariant Verification: Dependency Inversion & Isolation
1. **Zero Downward Leakage:**
   - `EricksonLopez.RateLimiting` (Core Tier 0) has **ZERO** references to `StackExchange.Redis`, `Microsoft.AspNetCore.App`, or any transport/serialization library.
   - Core depends exclusively on `EricksonLopez.Result` (domain result primitive) and Microsoft abstractions (`Logging.Abstractions`, `DependencyInjection.Abstractions`).
2. **Clean Project References:**
   - `EricksonLopez.RateLimiting.AspNetCore` references `EricksonLopez.RateLimiting`.
   - `EricksonLopez.RateLimiting.Redis` references `EricksonLopez.RateLimiting`.
   - Neither integration package references the other.
3. **No Circular Dependencies:**
   - The graph is strictly a Directed Acyclic Graph (DAG).
4. **Strong Naming:**
   - All three projects inherit strong-naming from `Directory.Build.props` (`EricksonLopez.snk`, PublicKey configured globally, `SignAssembly=true`).

---

## 3. External Dependencies Inventory & Threat Matrix

| Package | Version | Layer | Impact on Security / Performance / Reliability | Threat / Risk Assessment |
|---|---|---|---|---|
| `EricksonLopez.Result` | `2.0.0` | Core | Monadic `Result<T>` struct for railway-oriented programming. | **Low Risk:** Struct-based, zero allocation on success, verified in ecosystem. |
| `Microsoft.Extensions.Logging.Abstractions` | `10.0.11` | Core / Redis | High-performance `ILogger` interfaces and `LoggerMessage` source generators. | **Zero Risk:** Official BCL abstractions, trim-safe, AOT compatible. |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.11` | Core / Redis | `IServiceCollection` extension targets. | **Zero Risk:** Standard abstraction. |
| `Microsoft.Extensions.Options` | `10.0.11` | Redis | `IOptions<T>` binding. | **Low Risk:** BCL options model. |
| `StackExchange.Redis` | `2.8.47` | Redis | Distributed connection multiplexing, socket communication, Lua execution. | **High Criticality:** Outages, socket exhaustion, threadpool starvation, or Lua timeout directly impact availability. Fail-Open/Fail-Closed handling is critical. |
| `Microsoft.AspNetCore.App` | `Framework` | AspNetCore | HTTP Pipeline, `HttpContext`, Endpoint Routing metadata. | **Zero Risk:** Official framework reference. |

---

## 4. Test & Benchmark Dependencies

| Package | Version | Scope | Purpose |
|---|---|---|---|
| `Microsoft.NET.Test.Sdk` | `18.9.0` | Tests | Test host runner. |
| `xunit` | `2.9.3` | Tests | Test framework. |
| `xunit.runner.visualstudio` | `4.0.0` | Tests | IDE & VSTest test adapter. |
| `AwesomeAssertions` | `9.6.0` | Tests | Fluent assertions. |
| `NSubstitute` | `5.3.0` | Tests | Mocking substrate for Redis and HTTP contexts. |
| `NetArchTest.Rules` | `1.3.2` | Tests | Architecture rule validation (circular references, dependency direction). |
| `coverlet.collector` | `10.0.1` | Tests | Code coverage collector. |
| `BenchmarkDotNet` | `0.15.8` | Benchmarks | Microbenchmark harness. |
| `System.Threading.RateLimiting` | `10.0.11` | Benchmarks | BCL baseline comparative target. |
| `Microsoft.Extensions.TimeProvider.Testing` | `10.1.0` | Tests | `FakeTimeProvider` for clock-freeze and instant time progression. |
