# ADR-001: Distributed Rate Limiting

## Status
Accepted

## Date
2026-09-04

## Context
Exposing APIs to external consumers without robust rate limiting invites abuse, accidental Denial of Service (DoS) attacks via infinite loops, and resource starvation for legitimate users. To protect multi-tenant infrastructure, we need a rate limiting policy that is applied universally, consistently, and effectively across distributed instances.

ASP.NET Core 7.0+ introduced built-in Rate Limiting middleware, but out-of-the-box it primarily supports in-memory stores or relies on unoptimized configuration. We must define how rate limiting interacts with our specific authentication (tenant and user resolution) and how it fits into the `EricksonLopez.*` ecosystem.

## Decision
We will establish `EricksonLopez.RateLimiting` as the canonical implementation for API resilience.

1. **Partitioning by Identity**: Rate limits MUST be partitioned based on a strictly evaluated identity hierarchy:
   - Authenticated User (Subject ID)
   - Tenant ID (Company / Organization)
   - IP Address (for anonymous endpoints only)
2. **Pipeline Integration**: The rate limiting middleware `EricksonLopez.RateLimiting.AspNetCore` MUST be injected into the ASP.NET Core pipeline, evaluating endpoint metadata (`[EnableRateLimiting]`, `[DisableRateLimiting]`) while supporting global fallback policies.
3. **Rejection Protocol**: Rejected requests MUST return HTTP `429 Too Many Requests` along with standardized `Retry-After`, `X-RateLimit-Limit`, `X-RateLimit-Remaining`, and `X-RateLimit-Reset` headers. Custom formats (e.g. RFC 7807 ProblemDetails) are supported via the configurable `OnRejected` delegate. They must NOT return generic 500 errors or leak internal infrastructure state.
4. **Canonical Abstraction**: The library architecture provides a unified contract `IRateLimiter`, allowing seamless interchangeability between in-memory limiters and the distributed provider `EricksonLopez.RateLimiting.Redis`.
5. **Resilient Degradation (Fail-Safe Protocol)**: In distributed scenarios where the Redis store experiences timeouts, network partitions, or transient failures, the rate limiter MUST NOT throw unhandled runtime exceptions. Instead, it must return `Result<RateLimitLease>.Failure(Error)`. The middleware applies deterministic degradation:
   - **Fail-Open (Default)**: Requests are allowed to proceed to preserve high-availability, triggering the `OnRedisFailure` callback and diagnostic telemetry.
   - **Fail-Closed (Opt-In)**: Requests are rejected with HTTP `503 Service Unavailable` when strict capacity protection is prioritized over availability.

## Consequences

### Positive
- **Guaranteed Isolation**: No single tenant or malicious user can exhaust the CPU or database connection pool.
- **Standards Compliant**: Consistent HTTP `429` responses improve client retry behavior and system predictability.
- **Zero Unhandled Outages**: Eliminates cascading API failures caused by Redis transient errors.

### Negative
- **Operational Complexity**: In a distributed multi-node environment, Redis must be provisioned and monitored as a shared state tier.

## Compliance
- All public-facing API endpoints MUST be protected by `EricksonLopez.RateLimiting` configurations. Anonymous endpoints must have aggressive IP-based limits to prevent abuse and denial-of-service from unauthenticated traffic.

## References
- [ADR-002: Package Existence & Invariant Justification](./adr-002-package-existence-justification.md)
- [ADR-003: Named Policies & Endpoint Routing](./adr-003-named-rate-limiting-policies-and-endpoint-routing.md)
- [ADR-004: Resilient Degradation & Middleware Callbacks](./adr-004-resilient-degradation-and-middleware-callbacks.md)
