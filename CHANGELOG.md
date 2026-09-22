<!-- Copyright © Erickson Lopez. MIT License. -->
# Changelog

All notable changes to `EricksonLopez.RateLimiting` are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.0.0] — 2026-09-22

Initial release of the `EricksonLopez.RateLimiting` ecosystem for .NET.

### Added

#### `EricksonLopez.RateLimiting` (Core)
- **`IRateLimiter` contract**: Core interface `Task<Result<RateLimitLease>> AcquireAsync(string key, int permits = 1, CancellationToken cancellationToken = default)` defining thread-safe permit acquisition.
- **`RateLimitLease` record struct**: Zero-allocation `readonly record struct` carrying `IsAcquired`, `RemainingPermits`, `RetryAfter?`, `ResetTime?`, `DisposeAction?`, and `Limit?`. Implements `IDisposable` with zero heap allocation to release concurrency permits deterministically. Supports tuple deconstruction (`Deconstruct`).
- **`RateLimiterOptions`**: Parameter configuration for discrete window limiters (`PermitLimit`, `Window`, `SegmentsPerWindow`, `MaxPartitions`) with strict precondition validation guards (`ArgumentOutOfRangeException`).
- **Sliding Window limiter** (`SlidingWindowRateLimiter`, `SlidingWindowPartition`): Segmented circular ring-buffer smoothing burst traffic and preventing 2x boundary spikes.
- **Token Bucket limiter** (`TokenBucketRateLimiter`, `TokenBucketPartition`): Continuous replenishment rate limiter using fractional double precision arithmetic based on elapsed seconds.
- **Fixed Window limiter** (`FixedWindowRateLimiter`, `FixedWindowPartition`): High-throughput discrete counter algorithm for calendar-aligned quotas.
- **Concurrency limiter** (`ConcurrencyRateLimiter`, `ConcurrencyRateLimiterOptions`, `ConcurrencyPartition`): Restricts concurrent in-flight executions per partition key via lock-free `Interlocked.CompareExchange`. Rejects immediately when capacity is reached (zero queueing, bounded latency).
- **Composite multi-interval limiter** (`CompositeRateLimiter`): Sequential AND-logic evaluation across child limiters with automatic rollback on rejection and aggregate metadata (`min(RemainingPermits)`, `max(ResetTime)`).
- **Named Policy Registry** (`IRateLimiterPolicyRegistry`, `RateLimiterPolicyRegistry`, `IRateLimiterPolicy`, `RateLimiterPolicy`): Thread-safe `ConcurrentDictionary`-backed registry supporting case-insensitive ordinal lookup and fallback default limiter.
- **Fluent Policy Builder** (`RateLimiterPolicyBuilder`): Fluent configuration API (`AddFixedWindow`, `AddSlidingWindow`, `AddTokenBucket`, `AddConcurrency`, `AddComposite`, `AddPolicy`, `SetDefaultPolicy`) with optional `TimeProvider` injection for deterministic unit testing.
- **OpenTelemetry metrics** (`RateLimitingMetrics`): Native `System.Diagnostics.Metrics.Meter` instrumentation emitting `rate_limit.requests.total` (counter) and `rate_limit.lease.duration` (histogram) with zero heap allocation using `TagList` structs passed by `in` reference.
- **Error Code Constants** (`RateLimitingErrorCodes`): Public static class providing string constants (`ConnectionFailedCode`) for type-safe pattern matching in failure callbacks.
- **Dependency Injection extensions** (`RateLimitingServiceCollectionExtensions`): Extension methods (`AddSlidingWindowRateLimiter`, `AddTokenBucketRateLimiter`, `AddFixedWindowRateLimiter`, `AddConcurrencyRateLimiter`, `AddCompositeRateLimiter`).

#### `EricksonLopez.RateLimiting.AspNetCore`
- **`RateLimitingMiddleware`**: ASP.NET Core middleware executing policy evaluation, injecting standard `X-RateLimit-*` response headers (`X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`, `Retry-After`), and managing the deterministic `using var lease` lifecycle.
- **`RateLimitingMiddlewareOptions`**: Middleware configuration options for `PartitionKeyResolver`, `PermitCost`, `FailClosed` (503 Service Unavailable), `OnRejected` (custom ProblemDetails RFC 7807), and `OnRedisFailure`.
- **Named Policies & Endpoint Routing**: `[EnableRateLimiting(policyName)]` and `[DisableRateLimiting]` attributes; `.RequireDistributedRateLimiting(policyName)` and `.DisableDistributedRateLimiting()` Minimal API endpoint convention builder extensions.
- **Fluent Middleware DI**: `AddRateLimiting(Action<RateLimiterPolicyBuilder>, Action<RateLimitingMiddlewareOptions>?)` and `UseHttpRateLimiting()` application pipeline builder extensions.

#### `EricksonLopez.RateLimiting.Redis`
- **Redis Sliding Window** (`RedisSlidingWindowRateLimiter`, `RedisRateLimiterOptions`): Distributed sliding window rate limiter executing an atomic Lua script over Redis Sorted Sets (ZSET) in a single round-trip with microsecond-exact `Retry-After` calculation.
- **Redis Token Bucket** (`RedisTokenBucketRateLimiter`, `RedisTokenBucketRateLimiterOptions`): Distributed token bucket executing an atomic Lua script over Redis Hashes with fractional double replenishment and self-pruning `PEXPIRE` TTL.
- **Dependency Injection extensions** (`RateLimitingRedisServiceCollectionExtensions`): Overloads for `AddRedisRateLimiting` and `AddRedisTokenBucketRateLimiting` supporting connection strings or pre-existing `IConnectionMultiplexer` singletons.

#### Security & Hardening
- **Neutralized Redis Sliding Window Collision Bypass (CRITICAL — CWE-362)**: Injected unique cryptographically unforgeable GUID salt (`ARGV[6]`) into the Lua ZSET member constructor, preventing identical-millisecond concurrent requests from collapsing into duplicate members.
- **Neutralized Concurrency Limiter Double-Disposal Exploit (HIGH — CWE-675)**: Wrapped lease disposal in an atomic `OneShotDisposer` with `Interlocked.Exchange(ref _disposed, 1) == 0` preventing active permits from being released multiple times.
- **Neutralized Integer Wrap-Around Quota Bypass (HIGH — CWE-190)**: Replaced naive additions with overflow-safe bounds checks (`permits <= limit && count <= limit - permits`) across `FixedWindowPartition`, `SlidingWindowPartition`, and `ConcurrencyPartition`.
- **Neutralized Memory Exhaustion DoS (HIGH — CWE-770)**: Implemented bounded partition collections via `MaxPartitions` (default: 10,000) and periodic idle partition pruning (`IsIdle`) across all in-memory limiters.
- **Resilient Redis Network Fault Interception (MEDIUM — CWE-703)**: Broadened catch filters in `RedisSlidingWindowRateLimiter` and `RedisTokenBucketRateLimiter` to capture `TimeoutException`, `SocketException`, `ObjectDisposedException`, and `InvalidCastException` gracefully.
- **CancellationToken Enforcement (MEDIUM — CWE-400)**: Enforced early cancellation token evaluation (`cancellationToken.ThrowIfCancellationRequested()`) at method entry points across all `IRateLimiter` implementations.
- **IETF RFC 9651 Quota Reporting (MEDIUM — Standards Compliance)**: Enhanced `RateLimitLease` with `int? Limit` and updated `RateLimitingMiddleware` to format `X-RateLimit-Limit` using the policy quota instead of single-request permit cost.
- **Lua Command Modernization & Zero-Guard (LOW — CWE-369)**: Migrated Redis Token Bucket script from deprecated `HMSET` to `HSET` and clamped arithmetic to eliminate division-by-zero crashes on zero replenishment durations.

#### Quality & Architecture Verification
- **Native AOT & Trimming Compatibility**: Fully certified with `<IsAotCompatible>true</IsAotCompatible>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`. Dedicated Native AOT smoke test project (`EricksonLopez.RateLimiting.AotSmokeTest`) and CI workflow (`aot-smoke-test.yml`).
- **Strong-Named Assemblies**: Signed with official strong naming key (`EricksonLopez.snk`, `PublicKeyToken=f3a287785b2818a1`).
- **Multi-Targeting**: Simultaneous first-class target framework support across `.NET 10` (Modern LTS), `.NET 9` (STS), and `.NET 8` (Enterprise LTS).
- **Adversarial Security Test Suites**: Comprehensive test suites (`AdversarialTests.cs`, `AspNetCoreAdversarialTests.cs`, `RedisAdversarialTests.cs`) simulating Red Team attacks, integer overflow bypasses, and distributed collisions.
- **Mathematical Invariant & Property Tests**: Property-based validation in `InvariantMathematicalTests.cs` verifying the 6 formal rate limiting mathematical invariants.
- **Chaos Engineering & Fault Injection**: Full verification of Fail-Open and Fail-Closed modes under simulated Redis outages, connection drops, and socket resets.
- **Quality Gates & Governance**: Automated weekly dependency monitoring via Dependabot, Codecov integration with 95% target threshold, Stryker mutation testing quality gate, and BenchmarkDotNet regression gating.
