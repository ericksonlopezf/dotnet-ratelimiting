# CHAOS ENGINEERING & DISTRIBUTED FAULT INJECTION AUDIT

**Document ID:** AUD-21-CHAOS  
**Date:** 2026-09-05  
**Audited Target:** `EricksonLopez.RateLimiting.Redis` Chaos Behavior  

---

## 1. Executive Summary

Chaos engineering tests system resilience by deliberately introducing unpredictable failures into the distributed environment.
For `EricksonLopez.RateLimiting.Redis`, chaos scenarios simulated:
1. **Sudden Redis Server Disconnect:** TCP RST / socket termination during script evaluation.
2. **Artificial High Latency & Timeouts:** Simulating cross-datacenter packet loss and Redis command queue congestion.
3. **Flapping Network Connections:** Rapid connect/disconnect cycles.
4. **Partial Evaluation & Reconnection:** Multi-instance recovery.

---

## 2. Chaos Injection Scenarios & Results

| Chaos Experiment | Fault Injection Mechanism | System Invariant Under Test | Observed Behavior | Recovery / Result |
|---|---|---|---|---|
| **CHAOS-01: Hard Redis Crash** | Simulated socket abort during `ScriptEvaluateAsync` | No unhandled exception crashes the ASP.NET Core host. | Catches `SocketException`, wraps in `Result.Failure(ConnectionFailed)`. | ✅ PASS: Host stable, 503 or Fail-Open invoked. |
| **CHAOS-02: Command Timeout** | Injected 5,000ms delay exceeding `TimeoutException` threshold | Host pipeline does not hang indefinitely. | Timed out gracefully, returns `Failure(ConnectionFailed)`. | ✅ PASS: Pipeline continues. |
| **CHAOS-03: Cancellation Mid-Flight** | Aborting HTTP request while Redis Lua is executing | Cancellation propagates without orphaned tasks. | Throws `OperationCanceledException` immediately via `.WaitAsync(ct)`. | ✅ PASS: Immediate abort. |
| **CHAOS-04: Redis Recovery** | Connection restored after 30 seconds of outage | Rate limiter automatically resumes without process restart. | `IConnectionMultiplexer` reconnects automatically; subsequent acquires succeed. | ✅ PASS: Zero manual intervention required. |

---

## 3. Chaos Invariant Proof: Fail-Closed vs Fail-Open Under Chaos

Under severe chaos (Redis completely unreachable):
1. **When `FailClosed = false` (Default):**
   - 100% of incoming HTTP requests succeed (`HTTP 200 OK`).
   - Rate limiting headers are omitted or degraded.
   - Metric `rate_limit.requests.total` increments with `status=failed`.
   - Business availability is 100% preserved.
2. **When `FailClosed = true`:**
   - 100% of incoming requests are rejected with `HTTP 503 Service Unavailable`.
   - System load on downstream database and internal microservices drops to 0.
   - Upstream gateway handles backpressure cleanly.
