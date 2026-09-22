# REGRESSION TEST SUITE GUIDE

Covers regression verification for all audit findings:
- `FINDING-DOS-01`: `AdversarialRegressionTests.PartitionSaturation_UnderHighKeyFlooding`
- `FINDING-ARCH-02`: `CompositeRateLimiterTests.Rollback_WithWindowedLimiters_DocumentsPermitBurnBehavior`
- `FINDING-PERF-01`: `RateLimiterHotPathBenchmarks`
- `FINDING-SEC-01`: `RateLimitingMiddlewareTests.DefaultPartitionKeyResolver_ResolvesRemoteIpOrAnonymous`
- `FINDING-REDIS-01`: `RedisSlidingWindowRateLimiterTests.SlidingWindow_ShouldReject_WhenLimitExceeded`
- `FINDING-MUT-01`: `CompositeRateLimiterTests.AcquireAsync_ChildRejectsWithNullRetryAfter_DefaultsToOneSecond`
