# EVIDENCE: FAIL-OPEN & FAIL-CLOSED CHAOS VERIFICATION

**Target:** `RateLimitingMiddleware` with injected `RedisConnectionException`  

---

## 1. Test Log: Fail-Open Mode (`FailClosed = false`)
```text
[WARN] Redis rate limiter failed for key 192.168.1.50. StackExchange.Redis.RedisConnectionException: Socket closed.
[INFO] RateLimitingMiddleware: Degraded mode active. Request allowed to proceed to downstream endpoint.
[HTTP] 200 OK (Downstream endpoint executed successfully)
```

## 2. Test Log: Fail-Closed Mode (`FailClosed = true`)
```text
[ERROR] Redis rate limiter failed for key 192.168.1.50. StackExchange.Redis.RedisConnectionException: Socket closed.
[INFO] RateLimitingMiddleware: Fail-closed mode active. Rejecting request with HTTP 503.
[HTTP] 503 Service Unavailable
[Content-Type] application/json
[Body] {"code":"RateLimit.Redis.ConnectionFailed","error":"Redis operation failed: Socket closed."}
```
