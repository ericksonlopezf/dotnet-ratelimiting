<!-- Copyright © Erickson Lopez. MIT License. -->
# Security Policy

`EricksonLopez.RateLimiting` takes software security and vulnerability management seriously. As an edge and infrastructure throttling framework protecting high-throughput distributed systems, maintaining robust defenses against Denial of Service (DoS), algorithmic complexity attacks, and unauthorized state tampering is a primary invariant.

---

## Supported Versions

Security updates and critical patches are actively provided for the following releases:

| Version | Supported | Target Frameworks | Status |
|---|:---:|---|---|
| **1.2.x** | ✅ | .NET 8.0, .NET 9.0, .NET 10.0 | Current Stable Release |
| **1.0.x** | ⚠️ | .NET 8.0, .NET 9.0, .NET 10.0 | Maintenance Patches Only |
| **< 1.0.0** | ❌ | — | Unsupported |

---

## Reporting a Vulnerability

If you identify a security vulnerability, flaw, or potential exploit in `EricksonLopez.RateLimiting` or any of its sub-packages (`EricksonLopez.RateLimiting.AspNetCore`, `EricksonLopez.RateLimiting.Redis`), please report it privately.

**DO NOT report security vulnerabilities via public GitHub Issues, Discussions, or Pull Requests.**

### Private Security Reporting Channel

Please report all vulnerabilities via email to:

📧 **[ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com)**

Please include:
1. **Description**: Summary of the vulnerability and potential impact.
2. **Reproducible Proof of Concept**: Minimal sample code or steps to trigger the issue.
3. **Affected Components**: Specific package name(s) and version(s).
4. **Environment**: .NET runtime version, OS, Redis version (if applicable).
5. **Mitigation**: Any proposed remediation or temporary workaround.

You will receive an acknowledgment within 48 hours, followed by regular updates until a coordinated disclosure and patch release are finalized.

---

## Supply Chain Security

The build and release pipelines enforce multi-layered supply chain defenses:

1. **Strong Name Signing**:
   - All production assemblies are strongly signed using the official key `EricksonLopez.snk` (`PublicKeyToken=f3a287785b2818a1`).
   - Strong naming guarantees assembly identity, integrity, and tamper-detection across all supported target frameworks (`net8.0`, `net9.0`, `net10.0`).
2. **SourceLink & Symbol Verification**:
   - `Microsoft.SourceLink.GitHub` (v8.0.0) is centrally configured via CPM (`Directory.Packages.props`) and applied to all packable projects.
   - Symbols are packaged into companion `.snupkg` files (`<SymbolPackageFormat>snupkg</SymbolPackageFormat>`), enabling source code verification directly against the committed Git SHA.
3. **Package Integrity Gate**:
   - The CI pipeline (`ci.yml`) validates each generated `.nupkg` by unzipping the archive in an isolated environment and verifying the presence and integrity of `README.md` and `icon.png`.
   - Release publication enforces `--skip-duplicate` to prevent accidental overwrites or replay attacks on NuGet Gallery.

---

## Known Security Boundaries & Hardening Invariants

`EricksonLopez.RateLimiting` operates at the edge of application services. Its security architecture enforces the following defensive invariants:

1. **Denial of Service (DoS) Resistance (CWE-770)**:
   - In-memory rate limiters enforce bounded partition collections via `MaxPartitions` (default: 10,000). When capacity is exceeded, an atomic sweep (`PruneIdlePartitions`) removes idle keys, neutralizing unbounded memory growth from high-cardinality attacks (e.g. spoofed IP floods).
2. **Overflow-Safe Quota Arithmetic (CWE-190)**:
   - Partition counters and permit calculations evaluate safe subtraction bounds (`permits <= limit && count <= limit - permits`), preventing integer wrap-around bypasses.
3. **Cryptographic Collision Resistance (CWE-362)**:
   - The distributed Redis Sliding Window Lua script injects a cryptographically unforgeable GUID salt (`ARGV[6]`) into each Sorted Set (`ZSET`) member, preventing concurrent requests arriving at the identical millisecond timestamp from overwriting one another.
4. **Deterministic Lease Disposal & Concurrency Slot Safety (CWE-675)**:
   - Leases in `ConcurrencyRateLimiter` utilize an atomic `OneShotDisposer` with `Interlocked.Exchange(ref _disposed, 1) == 0`, ensuring active permit slots cannot be double-released or manipulated.
5. **Resilient Failure Degradation (CWE-703)**:
   - Network partitions, socket timeouts, and connection drops in Redis are intercepted functionally via `Result<RateLimitLease>.Failure(Error)`. The application pipeline degrades gracefully into either Fail-Open (default) or Fail-Closed (HTTP 503) without throwing unhandled exceptions.
6. **Cancellation Token Propagation (CWE-400)**:
   - All `IRateLimiter.AcquireAsync` implementations evaluate `cancellationToken.ThrowIfCancellationRequested()` at method entry points, preventing orphaned work and thread pool starvation under client disconnections.
7. **Native AOT & Trimming Safety**:
   - All production assemblies compile under `<IsAotCompatible>true</IsAotCompatible>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` with zero runtime reflection, zero dynamic code emission, and zero trim warnings.
