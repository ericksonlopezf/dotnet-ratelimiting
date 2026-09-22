# EVIDENCE: REVERSE PROXY & HEADER SPOOFING EXPERIMENT

**Test Case:** `X-Forwarded-For` Injection Attack  
**Target:** `RateLimitingMiddleware.InvokeAsync`  
**Execution Environment:** `Microsoft.AspNetCore.TestHost`  

---

## 1. Test Description
Evaluates whether injecting untrusted `X-Forwarded-For: 192.168.1.100` allows an attacker to rotate their partition key.

```csharp
var context = new DefaultHttpContext();
context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1"); // Ingress Proxy IP
context.Request.Headers["X-Forwarded-For"] = "192.168.1.100"; // Spoofed IP

var options = new RateLimitingMiddlewareOptions();
var key = options.PartitionKeyResolver(context);

Assert.Equal("10.0.0.1", key); // Resolves RemoteIpAddress directly, ignoring X-Forwarded-For
```

## 2. Experimental Result
- **Result:** **CONFIRMED.** Default key resolver is immune to raw spoofed headers, but binds all traffic to the proxy IP if `UseForwardedHeaders` is omitted.
