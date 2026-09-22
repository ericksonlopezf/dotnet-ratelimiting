# ATTACK PROOF-OF-CONCEPT: CAS ABA RETIREMENT RACE CONCURRENCY

**Target:** `ConcurrencyPartition` state machine  
**Vector:** Forcing thread collision between `TryRetire` (pruning) and `TryAcquireEx` (leasing)  
**Observed Result:** State transition $-1$ is atomic and final; thread collision cleanly recycles partition. Zero ABA vulnerability found.
