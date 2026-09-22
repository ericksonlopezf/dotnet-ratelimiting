# ATTACK PROOF-OF-CONCEPT: KEY POISONING & CARDINALITY EXPLOSION

**Target:** In-Memory Partition Dictionary  
**Vector:** Dispatching 100,000 distinct randomized string keys  
**Observed Result:** Dictionary size strictly capped at `MaxPartitions`. No memory exhaustion observed.
