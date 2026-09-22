# Phase 0 · Repository Discovery and Classification

This document formalizes the architectural discovery and comprehensive taxonomy of the `dotnet-ratelimiting` (`EricksonLopez.RateLimiting`) repository, establishing strict boundaries between the production libraries defining the public API surface and auxiliary projects.

---

## 1. General Identification

| Element | Identifier / Location | Description |
|---|---|---|
| **Primary Solution** | `EricksonLopez.RateLimiting.slnx` | Visual Studio / .NET 10 SLNX format |
| **Target Frameworks** | `.NET 8.0`, `.NET 9.0`, `.NET 10.0` | Multi-targeting with full Native AOT and Trimming support |
| **Language** | C# 13 / C# 12 | `Nullable` enabled, modern C# idiom |
| **License** | MIT License | Copyright © Erickson Lopez |
| **Organization** | `EricksonLopez` / `ericksonlopez.dev` | High-performance infrastructure libraries |

---

## 2. Taxonomic Classification of Projects

In accordance with architectural governance rules, every project is classified under a single canonical category:

| Project | Path | Classification | Role in Ecosystem |
|---|---|---|---|
| `EricksonLopez.RateLimiting` | `src/EricksonLopez.RateLimiting/` | **Core Library** | Primary in-memory rate limiting engine, algorithms (Fixed Window, Sliding Window, Token Bucket, Concurrency), lease records, policy builder, OpenTelemetry metrics, and DI extensions. |
| `EricksonLopez.RateLimiting.AspNetCore` | `src/EricksonLopez.RateLimiting.AspNetCore/` | **Infrastructure** | Native ASP.NET Core pipeline integration: middleware, metadata attributes, endpoint routing conventions, and standard HTTP headers. |
| `EricksonLopez.RateLimiting.Redis` | `src/EricksonLopez.RateLimiting.Redis/` | **Infrastructure** | Distributed Redis rate limiters (StackExchange.Redis): atomic Lua sliding window over Sorted Sets (ZSET) and atomic Lua token bucket over Hashes. |
| `EricksonLopez.RateLimiting.Showcase` | `samples/Showcase/` | **Samples (Showcase)** | Official reference implementation, executable documentation, and progressive testbed. **Maintained and integrated into the solution.** |
| `SlidingWindow.Sample` | `samples/SlidingWindow.Sample/` | **Samples** | Minimal demonstration of the Sliding Window algorithm in ASP.NET Core Minimal APIs. |
| `Redis.MultiTenant.Sample` | `samples/Redis.MultiTenant.Sample/` | **Samples** | Hierarchical multi-tenant resolution sample (Tenant Header -> User Subject -> Client IP). |
| `FailOpen.Sample` | `samples/FailOpen.Sample/` | **Samples** | Operational resilience and high availability demonstration (Fail-Open vs Fail-Closed, `OnRedisFailure` callback, RFC 7807). |
| `NamedPolicies.Sample` | `samples/NamedPolicies.Sample/` | **Samples** | Demonstration of configuring and applying multiple named policies in Minimal APIs. |
| `EricksonLopez.RateLimiting.Tests` | `tests/EricksonLopez.RateLimiting.Tests/` | **Tests** | Unit tests, concurrency tests, and mathematical invariant verification for the in-memory core. |
| `EricksonLopez.RateLimiting.AspNetCore.Tests` | `tests/EricksonLopez.RateLimiting.AspNetCore.Tests/` | **Tests** | Middleware tests, endpoint routing conventions, metadata attributes, and HTTP header assertions. |
| `EricksonLopez.RateLimiting.Redis.Tests` | `tests/EricksonLopez.RateLimiting.Redis.Tests/` | **Tests** | Unit tests, adversarial simulation, and Lua script testing against Redis. |
| `EricksonLopez.RateLimiting.Benchmarks` | `benchmarks/EricksonLopez.RateLimiting.Benchmarks/` | **Benchmarks** | Micro and macro benchmarks with BenchmarkDotNet evaluating latency, throughput, and zero heap allocation. |

> **Governance Principle:** Only projects categorized as **Core Library** (`EricksonLopez.RateLimiting`) and **Infrastructure** (`EricksonLopez.RateLimiting.AspNetCore` and `EricksonLopez.RateLimiting.Redis`) constitute the **Source of Truth** for the public API contract.

---

## 3. Documentation Catalog and Existing Artifacts

The repository maintains the following documentation and governance artifacts:

1. `README.md` (Root): Package overview, feature matrix, supported algorithms, and quick start guides.
2. `CHANGELOG.md`: Historical release notes adhering to Keep a Changelog.
3. `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`: Open source governance, security reporting SLA, and community guidelines.
4. `docs/architectural-justification.md`: Technical justification for fundamental design decisions (Zero-allocation, Native AOT, lock-free thread-safety).
5. `docs/functional-parity-audit.md`: Parity audit against industry standards (including `System.Threading.RateLimiting`).
6. `docs/product-strategy.md`: Product vision, target developer personas, and strategic roadmap.
7. `docs/testing-roadmap.md`: Matrix of unit testing, mutation testing, stress testing, and chaos validation.
8. `docs/mega-audit/`: Comprehensive forensic audit of security, concurrency, performance, threat models, and pre-production certification.

---

## 4. Showcase Project Integration

The `Showcase` project (`samples/Showcase/EricksonLopez.RateLimiting.Showcase.csproj`) is formally integrated in `EricksonLopez.RateLimiting.slnx` under the `/samples/` solution folder:

```xml
<Folder Name="/samples/">
  <Project Path="samples/SlidingWindow.Sample/SlidingWindow.Sample.csproj" />
  <Project Path="samples/Redis.MultiTenant.Sample/Redis.MultiTenant.Sample.csproj" />
  <Project Path="samples/FailOpen.Sample/FailOpen.Sample.csproj" />
  <Project Path="samples/NamedPolicies.Sample/NamedPolicies.Sample.csproj" />
  <Project Path="samples/Showcase/EricksonLopez.RateLimiting.Showcase.csproj" />
</Folder>
```

This guarantees that any change to the core libraries is automatically validated during solution builds.
