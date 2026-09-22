# DOCUMENTATION INTEGRITY, DISCREPANCIES & GUIDANCE AUDIT

**Document ID:** AUD-24-DOC  
**Date:** 2026-09-05  
**Audited Targets:** `README.md`, `docs/`, `adr/`, and inline XML documentation  

---

## 1. Executive Summary

Documentation integrity measures whether public documentation, architectural decision records (ADRs), and README guides accurately reflect executable code reality. When documentation makes performance or security claims that contradict compiled code, developer trust collapses.

The documentation audit found **high overall maturity (Grade A)** across quick starts, ADRs, and configuration examples. However, **2 concrete discrepancies between documentation claims and executable reality were identified and cataloged**.

---

## 2. Documentation Discrepancies vs Executable Reality

### 2.1 Discrepancy 1: "Zero Heap Allocation" Claim
- **Documentation Claim:**
  - `README.md`: *"Zero heap allocations on the critical lease evaluation path."*
  - `docs/functional-parity-audit.md`: *"Zero heap allocation per lease check. Struct-based immutable lease returns."*
- **Executable Reality:**
  - While `RateLimitLease` is an allocation-free struct, every synchronous in-memory rate limiter returns `Task<Result<RateLimitLease>>`.
  - `Task.FromResult(Result<RateLimitLease>.Success(lease))` allocates a new 72-byte `Task` instance on the managed heap on **every single call**.
- **Severity:** Medium / Architectural Accuracy.
- **Action Required:** Update README documentation to clarify:
  *"RateLimitLease is a zero-allocation readonly record struct. In-memory methods allocate a single 72-byte Task wrapper on invocation; transitioning to ValueTask in v2.0 will achieve 100% zero-allocation synchronous fast paths."*

### 2.2 Discrepancy 2: Reverse Proxy Assumption in RemoteIpAddress
- **Documentation Claim:**
  - README shows `app.UseHttpRateLimiting()` without mentioning reverse proxies.
- **Executable Reality:**
  - The default key resolver uses `context.Connection.RemoteIpAddress`.
  - In containerized Kubernetes/Docker deployments behind ingress controllers (ALB, Nginx, Cloudflare), `RemoteIpAddress` is the IP of the ingress proxy.
  - Without `app.UseForwardedHeaders()`, all clients are throttled together.
- **Severity:** Medium / Operational Guidance.
- **Action Required:** Add a prominent callout in `README.md` instructing developers to configure `UseForwardedHeaders()` when deploying behind reverse proxies.

---

## 3. "When NOT to Use" Guidance Audit

High-grade documentation must explicitly advise developers **when NOT to use** specific strategies:

| Algorithm / Feature | Recommended When | When NOT to Use / Anti-Pattern |
|---|---|---|
| **Fixed Window** | High-throughput simple burst protection, batch jobs. | When traffic spikes across window boundaries (2x burst) cannot be tolerated. |
| **Sliding Window** | Smooth API rate limiting, public web endpoints. | When memory per partition must be absolutely minimal ($O(1)$ scalar vs array). |
| **Token Bucket** | APIs allowing bursts with smooth continuous refill. | When clients require strict discrete calendar resets (e.g. 10,000 req/calendar month). |
| **Concurrency Limiter**| Heavy resource-intensive endpoints (exports, AI models). | General HTTP throughput throttling (does not restrict total requests per minute). |
| **Redis Distributed** | Horizontally scaled APIs across multiple container pods. | Single-instance monoliths (in-memory is $1000\times$ faster with zero network hops). |
| **Fail Closed** | High-security financial APIs where unmetered traffic is fatal. | Consumer applications where business availability trumps rate limiting enforcement. |

---

## 4. Documentation Quality Scorecard

| Dimension | Score | Assessment |
|---|---|---|
| **Quick Start Clarity** | 10/10 | 3-line setup, working code samples. |
| **Architecture Documentation** | 9.5/10 | 8 formal ADRs in `docs/adr/`. |
| **Algorithm Descriptions** | 9.5/10 | Mathematical models clearly explained. |
| **Discrepancy Severity** | 8.0/10 | Deducted for 72-byte `Task` allocation claim divergence. |
| **Overall Documentation Score** | **92.5 / 100** | **EXEMPLARY WITH MINOR REFINEMENTS NEEDED** |
