# CONCURRENCY TEST SUITE ARCHITECTURE

Covers:
- `ConcurrencyRateLimiterTests.cs`: Parallel acquire storm, CAS exhaustion, OneShotDisposer idempotency.
- `MegaAuditAdversarialSuite.cs`: Concurrent acquire vs prune eviction race.
- Thread concurrency ladders from 1 to 1024 threads.
