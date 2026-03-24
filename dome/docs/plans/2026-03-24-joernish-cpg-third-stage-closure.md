# Joernish CPG Third-Stage Closure

**Status:** Completed on 2026-03-24

## Purpose

This stage hardened the production CPG after the initial cutover. The target codebase is `src/Core/CPG`, where the schema, generator output, graph runtime, overlays, and Roslyn frontend now live together.

## Closed Work

1. Full-name-aware stable IDs were completed for declaration and control-flow nodes.
2. Type-relations and call-graph passes were tightened to rely on graph facts instead of short-name accidents.
3. Schema legality metadata and generator parity checks were folded into the same core package.
4. Hot-path semantic passes moved onto graph indexes in `src/Core/CPG/Graph`.

## Verification

```bash
dotnet test src/Core/CPG/Tests/JoernishCpg.Tests.csproj
```

The package-level suite passed as the closure gate, and the broader repository verification continues through `tests/Dome.Tests/Dome.Tests.csproj`.
