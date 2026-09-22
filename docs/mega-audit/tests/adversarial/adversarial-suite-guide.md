# ADVERSARIAL TEST SUITE ARCHITECTURE & EXECUTION GUIDE

The adversarial test suite is distributed across:
1. `tests/EricksonLopez.RateLimiting.Tests/MegaAuditAdversarialSuite.cs`
2. `tests/EricksonLopez.RateLimiting.Tests/AdversarialTests.cs`
3. `tests/EricksonLopez.RateLimiting.Tests/AdversarialRegressionTests.cs`
4. `tests/EricksonLopez.RateLimiting.AspNetCore.Tests/MegaAuditAspNetCoreAdversarialSuite.cs`
5. `tests/EricksonLopez.RateLimiting.Redis.Tests/MegaAuditRedisAdversarialSuite.cs`

All adversarial tests execute via:
```bash
dotnet test --filter "Category=Adversarial|FullyQualifiedName~Adversarial"
```
