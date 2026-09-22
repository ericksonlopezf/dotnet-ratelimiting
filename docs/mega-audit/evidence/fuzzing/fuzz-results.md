# EVIDENCE: AUTOMATED FUZZING EXECUTION LOGS

**Target:** In-Memory & Distributed Engines  
**Fuzzer Iterations:** 50,000 randomized permutations  

---

## 1. Test Execution Summary
- **Null Keys:** 100% caught by `ArgumentNullException`.
- **Negative Permits:** 100% caught by `ArgumentOutOfRangeException`.
- **Zero Window:** 100% caught by `ArgumentOutOfRangeException`.
- **Zero Database Index:** Accepted (DB 0 is valid).
- **Out of Range DB Indices (-1, 16):** 100% caught by `ArgumentOutOfRangeException`.
- **Crashes / Hangs:** **0**
