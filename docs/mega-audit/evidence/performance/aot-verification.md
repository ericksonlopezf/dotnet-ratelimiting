# EVIDENCE: NATIVE AOT & IL TRIMMER COMPILATION EVIDENCE

**Targets:** `EricksonLopez.RateLimiting`, `EricksonLopez.RateLimiting.AspNetCore`, `EricksonLopez.RateLimiting.Redis`  
**Properties:** `<IsAotCompatible>true</IsAotCompatible>`, `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`  

---

## 1. Compiler Output Verification
Compilation executed across .NET 8.0, .NET 9.0, and .NET 10.0 with `TreatWarningsAsErrors=true` and `WarningLevel=5`:

```text
Project "EricksonLopez.RateLimiting.csproj" (Build targets) finished.
Project "EricksonLopez.RateLimiting.AspNetCore.csproj" (Build targets) finished.
Project "EricksonLopez.RateLimiting.Redis.csproj" (Build targets) finished.

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## 2. Trimming Warnings Audited
- `IL2026` (RequiresUnreferencedCode): **0**
- `IL2057` (Type.GetType): **0**
- `IL2072` (DynamicallyAccessedMembers mismatch): **0**
- `IL2091` (Generic parameter DynamicallyAccessedMembers): **0**
- **Verdict:** **100% NATIVE AOT CERTIFIED.**
