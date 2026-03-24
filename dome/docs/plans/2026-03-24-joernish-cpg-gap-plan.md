# Joernish CPG Gap Closure Plan

**Status:** Completed on 2026-03-24

## Goal

Close the remaining gaps between the in-repo CPG implementation and the Joern-style model that Dome depends on. The final implementation target is `src/Core/CPG`, not an external or prototype-only location.

## Closed Scope

- Schema and generated model alignment in `src/Core/CPG/Schema` and `src/Core/CPG/Generated`
- Graph runtime and overlay behavior in `src/Core/CPG/Graph` and `src/Core/CPG/Passes`
- Roslyn frontend coverage in `src/Core/CPG/Frontend`
- Verification through `src/Core/CPG/Tests/JoernishCpg.Tests.csproj`

## Final Outcome

1. Stable node identity is full-name-aware across the core declaration and control-flow nodes.
2. Overlay execution is idempotent and records canonical overlay state in `META_DATA.OVERLAYS`.
3. Control-flow, type-relations, and call-graph behavior are now expressed through the core `src/Core/CPG` overlays.
4. Generated model artifacts are aligned with the schema-driven source in the same package.

## Verification

The final closure baseline is:

```bash
dotnet test src/Core/CPG/Tests/JoernishCpg.Tests.csproj
dotnet test tests/Dome.Tests/Dome.Tests.csproj
```
