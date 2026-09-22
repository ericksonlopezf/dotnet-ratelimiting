# ATTACK PROOF-OF-CONCEPT: REDIS DISTRIBUTED SPLIT-BRAIN CONCURRENCY

**Target:** Multi-Node Redis Coordination  
**Vector:** Simulating simultaneous multi-instance requests to same key during Redis network jitter  
**Observed Result:** Redis atomic single-threaded script execution serializes all requests deterministically.
