# Joernish CPG Rewrite Record

**Status:** Closed on 2026-03-24

## Summary

This document records the cutover that moved the Joern-style CPG work into the production core. The implementation now lives in `src/Core/CPG`, and that directory is the single source of truth for the schema, generated node model, graph runtime, Roslyn frontend, overlays, and the dedicated CPG test suite.

## Final Structure

- Core package: `src/Core/CPG/Dome.Core.Cpg.csproj`
- Dedicated tests: `src/Core/CPG/Tests/JoernishCpg.Tests.csproj`
- Solution-level application tests: `tests/Dome.Tests/Dome.Tests.csproj`

## What Closed

1. The CPG runtime is no longer tracked as an isolated prototype artifact.
2. The schema, graph runtime, frontend passes, and default overlays are owned by `src/Core/CPG`.
3. Verification moved to repository-native entry points instead of prototype-only commands.

## Verification Baseline

Use these commands for the final steady-state validation:

```bash
dotnet test src/Core/CPG/Tests/JoernishCpg.Tests.csproj
dotnet test tests/Dome.Tests/Dome.Tests.csproj
```
