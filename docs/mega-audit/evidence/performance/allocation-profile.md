# EVIDENCE: ALLOCATION PROFILE & GC IMPACT

**Benchmark Suite:** `RateLimiterHotPathBenchmarks`  
**Host Process:** .NET 10.0.0 (10.0.26.11504), X64 RyuJIT AVX2  

---

## 1. Disassembly & Allocation Breakdown

```text
EricksonLopez.RateLimiting.FixedWindowRateLimiter.AcquireAsync(string key, int permits)
  ├── struct RateLimitLease (allocated on stack, 32 bytes)
  ├── struct Result<RateLimitLease> (allocated on stack, 40 bytes)
  └── Task.FromResult<Result<RateLimitLease>>(...) ──► ALLOCATES ON HEAP
      ├── Object Header: 8 bytes
      ├── MethodTable Pointer: 8 bytes
      ├── Result<RateLimitLease> Payload: 40 bytes
      ├── Task state fields: 16 bytes
      └── Total Allocated Heap Memory: 72 BYTES PER CALL
```

## 2. Quantitative Measurement (BenchmarkDotNet)
- `BCL_TokenBucket_AttemptAcquire`: **0 B / op** (Zero heap allocation).
- `EricksonLopez_TokenBucket_AcquireAsync`: **72 B / op** (Due to `Task<Result<T>>`).
- `EricksonLopez_FixedWindow_AcquireAsync`: **72 B / op**.
- `EricksonLopez_SlidingWindow_AcquireAsync`: **72 B / op**.
- `RateLimitLease_Struct_ZeroAllocation`: **0 B / op**.
