# Phase 7 · System Architectural Diagrams (Mermaid)

This document contains the official Mermaid diagrams reflecting the actual architecture and operational flows of `EricksonLopez.RateLimiting`.

---

## 1. General Architectural Diagram

```mermaid
graph TB
    subgraph ClientLayer["1. Ingress & Clients"]
        HTTP["HTTP Client / Browser / Mobile"]
        Job["Background Worker / Quartz / MassTransit"]
    end

    subgraph AspNetCore["2. EricksonLopez.RateLimiting.AspNetCore"]
        Middleware["RateLimitingMiddleware"]
        Resolver["PartitionKeyResolver (IP / Tenant / User)"]
        Meta["Endpoint Metadata (IEnable / IDisable)"]
        Headers["Header Formatter (X-RateLimit-*, Retry-After)"]
    end

    subgraph Core["3. EricksonLopez.RateLimiting (Core Engine)"]
        Registry["IRateLimiterPolicyRegistry / Builder"]
        Fixed["FixedWindowRateLimiter"]
        Sliding["SlidingWindowRateLimiter"]
        Token["TokenBucketRateLimiter"]
        Concurrency["ConcurrencyRateLimiter"]
        Composite["CompositeRateLimiter (AND Logic)"]
        Lease["RateLimitLease (record struct, IDisposable)"]
        Metrics["RateLimitingMetrics (Meter, Counter, Histogram)"]
    end

    subgraph Distributed["4. EricksonLopez.RateLimiting.Redis"]
        RedisSliding["RedisSlidingWindowRateLimiter"]
        RedisToken["RedisTokenBucketRateLimiter"]
        LuaScripts["Atomic Lua Scripts (ZSET / Hash)"]
        StackExchange["StackExchange.Redis Multiplexer"]
    end

    subgraph Storage["5. Persistence & Memory"]
        MemTables["ConcurrentDictionary Partitions (O(1))"]
        RedisServer[("Redis Server / Cluster")]
    end

    HTTP --> Middleware
    Job --> Core
    Middleware --> Meta
    Middleware --> Resolver
    Middleware --> Registry
    Registry --> Fixed
    Registry --> Sliding
    Registry --> Token
    Registry --> Concurrency
    Registry --> Composite
    Registry --> RedisSliding
    Registry --> RedisToken

    Fixed --> MemTables
    Sliding --> MemTables
    Token --> MemTables
    Concurrency --> MemTables

    RedisSliding --> LuaScripts
    RedisToken --> LuaScripts
    LuaScripts --> StackExchange
    StackExchange --> RedisServer

    Core --> Lease
    Core --> Metrics
    Lease --> Headers
    Headers --> HTTP
```

---

## 2. Main Request Flow Diagram (HTTP Request Evaluation)

```mermaid
flowchart TD
    Start(["Start: Incoming HTTP Request"]) --> CheckEndpoint{"Does endpoint have DisableRateLimiting?"}
    
    CheckEndpoint -- Yes --> PassThrough["Execute Downstream Pipeline (_next)"]
    CheckEndpoint -- No --> ResolveKey["Resolve Partition Key (IP / Tenant / User)"]
    
    ResolveKey --> CheckPolicy{"Does endpoint declare EnableRateLimiting(policy)?"}
    
    CheckPolicy -- Yes --> LookupNamed["Retrieve IRateLimiter from IRateLimiterPolicyRegistry"]
    CheckPolicy -- No --> LookupDefault["Retrieve DefaultLimiter or DI Registered Service"]
    
    LookupNamed --> HasLimiter{"Does limiter exist?"}
    LookupDefault --> HasLimiter
    
    HasLimiter -- No --> ErrPolicy["Throw InvalidOperationException (Configuration Error)"]
    HasLimiter -- Yes --> Acquire["Invoke AcquireAsync(key, permits)"]
    
    Acquire --> EvalResult{"Result Status?"}
    
    EvalResult -- Network/Redis Failure --> CheckFailureHandler{"Is OnRedisFailure configured?"}
    CheckFailureHandler -- Yes --> ExecOnRedisFail["Execute OnRedisFailure(context, error)"]
    CheckFailureHandler -- No --> CheckFailClosed{"Is FailClosed == true?"}
    CheckFailClosed -- Yes --> Ret503["Respond HTTP 503 Service Unavailable"]
    CheckFailClosed -- No --> PassFailOpen["Fail-Open: Continue to _next(context)"]
    
    EvalResult -- Success --> SetHeaders["Inject Headers: X-RateLimit-Limit, Remaining, Reset"]
    SetHeaders --> IsLeaseAcquired{"Is lease.IsAcquired == true?"}
    
    IsLeaseAcquired -- Yes --> PassThrough
    IsLeaseAcquired -- No --> SetRetryAfter["Inject Retry-After Header"]
    SetRetryAfter --> CheckOnRejected{"Is OnRejected configured?"}
    CheckOnRejected -- Yes --> ExecOnRejected["Execute OnRejected(context, lease)"]
    CheckOnRejected -- No --> Ret429["Respond HTTP 429 Too Many Requests JSON"]
    
    PassThrough --> End(["End: Response Delivered"])
    Ret429 --> End
    Ret503 --> End
    ExecOnRedisFail --> End
    ExecOnRejected --> End
    ErrPolicy --> End
```

---

## 3. Sequence Diagram (Component Interaction Under Concurrency)

```mermaid
sequenceDiagram
    autonumber
    actor Client as HTTP Client
    participant MW as RateLimitingMiddleware
    participant Resolver as PartitionKeyResolver
    participant Reg as PolicyRegistry
    participant Limiter as ConcurrencyRateLimiter
    participant Metrics as RateLimitingMetrics
    participant Downstream as Endpoint / Controller

    Client->>MW: HTTP GET /api/heavy-process
    MW->>Resolver: PartitionKeyResolver(context)
    Resolver-->>MW: "tenant:alpha"
    MW->>Reg: GetPolicy("heavy-policy")
    Reg-->>MW: ConcurrencyRateLimiter instance
    MW->>Limiter: AcquireAsync("tenant:alpha", permits: 1)
    
    critical Atomic Concurrency Evaluation
        Limiter->>Limiter: Interlocked Slot Allocation
    end

    alt Permit Acquired
        Limiter->>Metrics: RecordRequest("concurrency", "acquired", durationMs)
        Limiter-->>MW: Result.Success(RateLimitLease { IsAcquired = true })
        MW->>Client: Headers (X-RateLimit-Limit: 3, Remaining: 2)
        MW->>Downstream: _next(context)
        Downstream-->>MW: Response OK (200)
        MW->>Limiter: lease.Dispose() (Releases Concurrency Slot)
        MW-->>Client: HTTP 200 OK + Body
    else Permit Rejected (Saturated)
        Limiter->>Metrics: RecordRequest("concurrency", "rejected", durationMs)
        Limiter-->>MW: Result.Success(RateLimitLease { IsAcquired = false, RetryAfter: 50ms })
        MW->>Client: HTTP 429 Too Many Requests (Retry-After: 1)
    end
```

---

## 4. `RateLimitLease` State Lifecycle Diagram

```mermaid
stateDiagram-v2
    [*] --> Evaluating: AcquireAsync() invoked

    Evaluating --> SuccessfulAcquired: Permits available
    Evaluating --> Rejected: Quota exceeded / saturated
    Evaluating --> FailureState: Network failure / Redis timeout

    state SuccessfulAcquired {
        [*] --> ActiveLease: IsAcquired = true
        ActiveLease --> Consuming: Processing request in pipeline
        Consuming --> Disposed: using / Dispose() invoked
        Disposed --> ReleasedSlot: DisposeAction executed (releases concurrency)
    }

    state Rejected {
        [*] --> QuotaExceeded: IsAcquired = false
        QuotaExceeded --> HeadersCalculated: RetryAfter & ResetTime set
    }

    state FailureState {
        [*] --> InfraError: Result.IsFailure (e.g. ConnectionFailedCode)
    }

    ReleasedSlot --> [*]
    HeadersCalculated --> [*]
    InfraError --> [*]
```

---

## 5. Solution Dependency Diagram

```mermaid
graph TD
    subgraph External["External / BCL Libraries"]
        Bcl["System.Diagnostics.Metrics / BCL"]
        RedisLib["StackExchange.Redis (2.8+)"]
        AspCoreLib["Microsoft.AspNetCore.App"]
        ResultLib["EricksonLopez.Result"]
    end

    subgraph CoreLib["EricksonLopez.RateLimiting"]
        IRateLimiter["IRateLimiter"]
        RateLimitLease["RateLimitLease"]
        RateLimiterOptions["RateLimiterOptions"]
        Policies["EricksonLopez.RateLimiting.Policies"]
        MetricsClass["RateLimitingMetrics"]
    end

    subgraph AspNetCoreLib["EricksonLopez.RateLimiting.AspNetCore"]
        MiddlewareClass["RateLimitingMiddleware"]
        Attrs["Enable / Disable Attributes"]
        Extensions["Endpoint & Service Extensions"]
    end

    subgraph RedisLibPkg["EricksonLopez.RateLimiting.Redis"]
        RedisSlidingClass["RedisSlidingWindowRateLimiter"]
        RedisTokenClass["RedisTokenBucketRateLimiter"]
        RedisExtensions["RateLimitingRedisServiceCollectionExtensions"]
    end

    CoreLib --> ResultLib
    CoreLib --> Bcl

    AspNetCoreLib --> CoreLib
    AspNetCoreLib --> AspCoreLib

    RedisLibPkg --> CoreLib
    RedisLibPkg --> RedisLib
    RedisLibPkg --> ResultLib
```

---

## 6. CompositeRateLimiter Pipeline Diagram (Transactional Rollback)

```mermaid
flowchart TD
    Req(["Incoming Request"]) --> EvalChild1["1. Evaluate Limiter A (TokenBucket - Burst)"]
    
    EvalChild1 -- Approved --> EvalChild2["2. Evaluate Limiter B (SlidingWindow - Quota)"]
    EvalChild1 -- Rejected/Failure --> Abort1["Abort & Return Rejection"]
    
    EvalChild2 -- Approved --> EvalChild3["3. Evaluate Limiter C (Concurrency - In-Flight)"]
    EvalChild2 -- Rejected/Failure --> Rollback1["Rollback: Dispose Lease from Limiter A"]
    Rollback1 --> Abort2["Return Rejection B"]
    
    EvalChild3 -- Approved --> MergeLease["Consolidate Composite Lease (Min Remaining, Max Reset)"]
    EvalChild3 -- Rejected/Failure --> Rollback2["Rollback: Dispose Leases from Limiters A & B"]
    Rollback2 --> Abort3["Return Rejection C"]
    
    MergeLease --> Success(["Lease Granted with Combined DisposeAction"])
```
