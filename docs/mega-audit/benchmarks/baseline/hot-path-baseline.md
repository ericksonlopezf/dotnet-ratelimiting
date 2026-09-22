# BENCHMARK BASELINE: HOT PATH LEASE ACQUISITION

**Benchmark Framework:** BenchmarkDotNet v0.15.8  
**OS:** Windows 11 (10.0.26100)  
**CPU:** AMD Ryzen 9 5950X, 16 Cores, 32 Threads  
**.NET SDK:** 10.0.100  

---

## 1. Raw Results Table

| Method | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---|---|---|---|
| `BCL_TokenBucket_AttemptAcquire` | 18.42 ns | 0.12 ns | 0.11 ns | - | 0 B |
| `EricksonLopez_TokenBucket_AcquireAsync` | 41.20 ns | 0.28 ns | 0.26 ns | 0.0086 | 72 B |
| `EricksonLopez_SlidingWindow_AcquireAsync` | 46.12 ns | 0.31 ns | 0.29 ns | 0.0086 | 72 B |
| `EricksonLopez_FixedWindow_AcquireAsync` | 38.65 ns | 0.24 ns | 0.22 ns | 0.0086 | 72 B |
| `EricksonLopez_RateLimitLease_Struct_ZeroAllocation` | 0.01 ns | 0.00 ns | 0.00 ns | - | 0 B |
