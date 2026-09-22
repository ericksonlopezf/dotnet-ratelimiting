## Description
<!-- Provide a clear, concise summary of the changes introduced by this pull request. -->

## Type of Change
- [ ] `feat`: New feature or algorithm enhancement
- [ ] `fix`: Bug fix or security hardening
- [ ] `perf`: Performance optimization (requires benchmark evidence)
- [ ] `refactor`: Code refactoring without behavior change
- [ ] `docs`: Documentation addition or update
- [ ] `test`: New test cases or quality harness improvement

## Affected Packages
- [ ] `EricksonLopez.RateLimiting` (Core Engine)
- [ ] `EricksonLopez.RateLimiting.AspNetCore` (HTTP Middleware & Endpoint Conventions)
- [ ] `EricksonLopez.RateLimiting.Redis` (Distributed Redis Provider)
- [ ] `Samples / Benchmarks / Build Infrastructure`

## Quality Gates Checklist
- [ ] Architecture compliance verified (`pwsh ./scripts/verify-compliance.ps1` passes)
- [ ] Code formatted and verified (`dotnet format --verify-no-changes`)
- [ ] Unit and adversarial tests pass on all frameworks (`net8.0`, `net9.0`, `net10.0`)
- [ ] Zero-allocation contract verified on hot paths (0 B allocated for struct leases)
- [ ] Benchmark regression gate verified (latency regression $\le 5.0\%$ vs `baseline.json`)
- [ ] Stryker.NET mutation testing evaluated (meets $\ge 95\%$ break threshold)
- [ ] 100% Native AOT & Trimming compliant (zero IL2026 / IL3050 warnings)
- [ ] XML documentation comments added or updated for all public members (CS1591 clean)
- [ ] Technical documentation in `/docs/` updated using strict `kebab-case.md` naming
