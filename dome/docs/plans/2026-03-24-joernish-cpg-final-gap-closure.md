# Joernish CPG Final Gap Closure

**Status:** Completed on 2026-03-24

## Purpose

This document records the last high-value closure pass after the CPG became a first-class core package. The implementation and tests now live under `src/Core/CPG`.

## Closed Items

1. Dynamic dispatch resolution was tightened with receiver-aware and hierarchy-aware behavior.
2. Field-access linking was decoupled from fragile frontend naming assumptions.
3. CFG and CDG coverage expanded for explicit branch shapes.
4. Layer dependency handling aligned with deterministic skip semantics.

## Verification

```bash
dotnet test src/Core/CPG/Tests/JoernishCpg.Tests.csproj
dotnet test tests/Dome.Tests/Dome.Tests.csproj
```

This is the final repository-native verification baseline for the closed plan.
