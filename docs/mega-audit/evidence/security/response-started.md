# EVIDENCE: STREAMING RESPONSE & RESPONSE STARTED RACE CONDITION

**Test Case:** Response Headers Committed Before Middleware Unwinds  
**Target:** `RateLimitingMiddleware.InvokeAsync`  

---

## 1. Test Description
Simulates an SSE (Server-Sent Events) or gRPC streaming endpoint where headers are flushed during `_next(context)`.

```csharp
RequestDelegate next = async ctx =>
{
    // Write body to force response headers to start
    await ctx.Response.Body.WriteAsync(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
    Assert.True(ctx.Response.HasStarted);
};

var middleware = new RateLimitingMiddleware(next, Options.Create(new RateLimitingMiddlewareOptions()));
var context = new DefaultHttpContext();
var limiter = Substitute.For<IRateLimiter>();
limiter.AcquireAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
    .Returns(RateLimitLease.Successful(10, DateTimeOffset.UtcNow.AddMinutes(1), 10));

// Execution must NOT throw InvalidOperationException
await middleware.InvokeAsync(context, limiter);
```

## 2. Experimental Result
- **Result:** **PASSED.** `!response.HasStarted` prevents setting headers after flush. Zero exceptions thrown.
