# ADR-004: Resilient Degradation and Middleware Callbacks

## Status
Accepted

## Date
2026-09-04

## Context
A major operational vulnerability in distributed rate limiting is the failure mode of the shared state backend (e.g., Redis). When Redis experiences memory exhaustion, network timeouts, or leader failover:
- Libraries throwing raw exceptions cause incoming traffic to crash with HTTP 500 errors, taking down the entire API even though downstream business services may be healthy.
- Libraries silently failing open without alerting blind platform engineers, allowing traffic floods to go undetected until databases collapse.
- Fixed 429 response formats prevent enterprise systems from returning compliant RFC 7807 ProblemDetails or custom branded payload contracts.

To fulfill Invariant 3 (Railway-Oriented Degradation) and Invariant 5 (Strict Boundary Separation), `EricksonLopez.RateLimiting.AspNetCore` requires a deterministic, highly configurable failure lifecycle.

## Decision
We implement an explicit fail-safe degradation lifecycle and configurable delegates in `RateLimitingMiddlewareOptions` and `RateLimitingMiddleware`:

1. **Deterministic Return Values (`Result<RateLimitLease>`)**:
   - `RedisSlidingWindowRateLimiter` catches `RedisException` internally and returns `Result<RateLimitLease>.Failure(RateLimitErrors.ConnectionFailed(detail))`. It never throws unhandled exceptions during request evaluation.

2. **Middleware Failure Handling**:
   - When `leaseResult.IsFailure`:
     - **Custom Callback**: If `OnRedisFailure` (`Func<HttpContext, Error, CancellationToken, Task>`) is defined, it is invoked immediately. The middleware then returns without calling `_next` — the callback is responsible for writing the HTTP response or otherwise completing the request. To allow the request to proceed after observability work, do not define `OnRedisFailure` and rely on the `FailClosed` setting instead.
     - **Fail-Open (Default, `FailClosed = false`)**: If no callback is defined, the request is permitted to proceed down the pipeline (`await _next(context)`). This guarantees high availability during caching infrastructure disruptions.
     - **Fail-Closed (`FailClosed = true`)**: If configured, rejected requests terminate with HTTP `503 Service Unavailable` and structured error JSON (`RateLimit.Redis.ConnectionFailed`). Use `RateLimitingErrorCodes.ConnectionFailedCode` to match this code programmatically.

3. **Rejection Customization (`OnRejected`)**:
   - When a lease is rejected (`lease.IsAcquired == false`):
     - `X-RateLimit-Limit`: The total permit quota allocated for the active policy, extracted from `RateLimitLease.Limit` (introduced in v1.0.0 for IETF RFC 9651 compliance). If `lease.Limit` is null, it falls back to `RateLimitingMiddlewareOptions.PermitCost`.
     - `X-RateLimit-Remaining`: The remaining permits in the active window from `RateLimitLease.RemainingPermits`.
     - `X-RateLimit-Reset`: Unix epoch seconds of when the window resets, from `RateLimitLease.ResetTime`.
     - `Retry-After`: Seconds until retry is safe, computed from `RateLimitLease.RetryAfter`.
     - If `OnRejected` (`Func<HttpContext, RateLimitLease, CancellationToken, Task>`) is provided, execution delegates to it, allowing consumers to format RFC 7807 ProblemDetails responses, append custom headers, or send metrics.
     - If `OnRejected` is null, the middleware writes standard RFC 6585 JSON with HTTP `429 Too Many Requests`.

4. **Empty Key Guard**:
   - If `PartitionKeyResolver(context)` evaluates to null or whitespace, the middleware automatically substitutes `"anonymous"`, preventing null-key reference exceptions or Redis key format corruption.

## Consequences

### Positive
- **High Availability by Default**: Prevents caching layer blips from escalating into full API outages.
- **Operational Visibility**: Guarantees platform engineers can observe and alert on infrastructure degradation via callbacks and metrics.
- **RFC 7807 Compliance**: Consumers can seamlessly adhere to enterprise API error formats.

### Negative
- Operating in fail-open mode during an outage temporarily permits traffic beyond configured quotas until Redis connectivity recovers.

## References
- [ADR-001: Distributed Rate Limiting](./adr-001-distributed-rate-limiting.md)
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
