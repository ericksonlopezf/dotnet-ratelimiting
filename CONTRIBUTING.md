<!-- Copyright © Erickson Lopez. MIT License. -->
# Contributing to EricksonLopez.RateLimiting

Thank you for your interest in contributing to `EricksonLopez.RateLimiting`! We welcome contributions that align with our high standards for performance, thread safety, zero allocations, and enterprise-grade reliability.

---

## Architectural Principles & Invariants

Before submitting code, ensure your changes adhere to these core principles:

1. **Zero Allocations on the Hot Path**: Rate limit evaluations must never allocate on the managed heap. Leases are structs (`readonly record struct RateLimitLease`), and metric tags use `TagList` passed by `in` reference.
2. **Deterministic Thread Safety**: Concurrency limits and in-memory counters use lock-free atomic primitives (`Interlocked.CompareExchange`, `Volatile.Read`, `Interlocked.Exchange`) or discrete bounded locks.
3. **Native AOT & Trimming Certified**: All production code must compile under `<IsAotCompatible>true</IsAotCompatible>` with zero IL2026/IL3050 warnings. Dynamic reflection and dynamic IL emission are strictly prohibited.
4. **Railway-Oriented Result Pattern**: Never throw exceptions for operational flow control or expected failures. Operational errors return `Result<RateLimitLease>.Failure(Error)`.
5. **One Type Per File**: Each file in `src/` must declare exactly one top-level type matching the filename.
6. **No Obsolete APIs**: The solution enforces zero `[Obsolete]` members and zero compiler deprecation warnings.
7. **Complete XML Documentation**: All public types and members must include full XML documentation comments. `CS1591` is strictly treated as an error.
8. **Supply Chain Integrity**: Assemblies must be strongly signed with `EricksonLopez.snk` and integrated with `Microsoft.SourceLink.GitHub`.

---

## Development & Verification Workflow

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (builds and tests across `net8.0`, `net9.0`, and `net10.0`)
- PowerShell 7+ (`pwsh`) or Windows PowerShell
- Docker (optional, for running local Redis testing instances)

### Build and Test Commands

All commands are derived directly from the canonical CI pipeline (`.github/workflows/ci.yml`):

```bash
# 1. Restore dependencies with Central Package Management (CPM)
dotnet restore EricksonLopez.RateLimiting.slnx

# 2. Build solution in Release configuration with strict diagnostics
dotnet build EricksonLopez.RateLimiting.slnx --no-restore --configuration Release

# 3. Verify code style and formatting
dotnet format --verify-no-changes

# 4. Run automated architecture and compliance checks
pwsh ./scripts/verify-compliance.ps1

# 5. Run all test suites across all target frameworks with code coverage collection
dotnet test EricksonLopez.RateLimiting.slnx --no-build --configuration Release --verbosity normal --collect:"XPlat Code Coverage"

# 6. Validate NuGet packaging and package integrity
dotnet pack EricksonLopez.RateLimiting.slnx --no-build --configuration Release -o artifacts/
```

---

## Quality Gates & Verification Scripts

Every pull request is validated against strict quality gates:

### 1. Benchmark Regression Quality Gate
- Located in `benchmarks/EricksonLopez.RateLimiting.Benchmarks`.
- Run locally:
  ```bash
  dotnet run --project benchmarks/EricksonLopez.RateLimiting.Benchmarks/EricksonLopez.RateLimiting.Benchmarks.csproj --configuration Release --framework net10.0 -- --filter "*" --job short --exporters json --memory --artifacts ./benchmarks/pr-results
  pwsh ./scripts/verify-benchmark-gate.ps1 -ReportDir ./benchmarks/pr-results -BaselinePath ./benchmarks/results/baseline.json -MaxLatencyRegressionPercent 5.0
  ```
- **Invariants**:
  - **Heap Invariant**: Zero allocation (0 B) on all hot-path combinators and lease evaluations.
  - **Latency Invariant**: Mean execution latency must not regress more than 5.0% against `benchmarks/results/baseline.json`.

### 2. Stryker.NET Mutation Testing Quality Gate
- Configured via `stryker-core-config.json`, `stryker-aspnetcore-config.json`, and `stryker-redis-config.json`.
- Enforces strict mutation thresholds:
  - **High**: $\ge 100\%$
  - **Low**: $\ge 98\%$
  - **Break**: $\ge 95\%$ (PR fails if mutation score drops below 95%)
- Run locally for a specific project:
  ```bash
  dotnet stryker --config-file stryker-core-config.json
  ```

---

## Pull Request Guidelines

1. **Branch Naming**: Use descriptive prefixes matching repository conventions:
   - `feat/` — New features or algorithm enhancements
   - `fix/` — Bug fixes or security remediations
   - `docs/` — Documentation updates
   - `perf/` — Performance optimizations (must include benchmark diff)
   - `refactor/` — Code refactoring without behavior change
2. **Commit Messages**: Follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):
   - Example: `feat(ratelimiting): implement adaptive sliding window partition`
   - Example: `fix(redis): neutralize millisecond collision via GUID salt`
3. **Tests Required**: Every modification must be accompanied by comprehensive tests (unit tests, adversarial tests, or mathematical invariant tests).
4. **Documentation**: Update relevant Markdown documents in `docs/` using strict kebab-case naming.
5. **Code of Conduct**: All contributors are expected to adhere to our [Code of Conduct](./CODE_OF_CONDUCT.md).
6. **Security Vulnerabilities**: For sensitive security issues, follow [SECURITY.md](./SECURITY.md) and report privately.

---

## Questions & Contact

For architectural inquiries or maintainer correspondence:

📧 **[ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com)**
