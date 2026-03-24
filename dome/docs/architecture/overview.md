# Architecture Overview

Dome is a C# analysis, planning, and rewrite toolchain. The current architecture centers the code property graph in `src/Core/CPG`, then threads that model through recipe-based, fixed-slot application flows for analysis, planning, runtime, and reporting.

## Main Flows

1. Standard flow: analyze source, compile a plan, and emit rewrite output in `run` mode.
2. Runtime flow: prepare a runnable workspace on top of the standard flow.
3. Shadow extraction: slice from a seed member and build a minimal shadow workspace.

## Top-Level Modules

### `src/Core`

Core domain models and rule services live here. `src/Core/CPG` is the canonical home for the CPG schema, graph runtime, overlays, Roslyn frontend, and the dedicated CPG tests.

### `src/Application`

The application layer owns use-case orchestration and public ports. Flow execution still uses the shared pipeline kernel, but flow assembly is now recipe-based and fixed-slot instead of hand-authored stage arrays in each host:

- `Ports`: requests, results, and cross-layer contracts
- `Pipeline`: execution kernel, `FlowRecipe`, `FlowBuilder`, and standard Dome stages
- `UseCases/Runtime`: runtime-specific stages
- `UseCases/ShadowExtraction`: shadow-extraction-specific stages

### `src/Adapters`

Adapters implement the external-facing ports:

- `Analysis.Roslyn`: workspace loading and Roslyn-backed analysis
- `Rewrite.Roslyn`: source rewriting
- `Reporting.Json`: report and plan persistence
- `Runtime.Process`: runtime workspace preparation and build execution

### `apps`

Composition roots and executable hosts. Each host selects a recipe, binds slot adapters, and delegates execution to the shared flow runner:

- `Dome.Application`
- `Dome.Application.Runtime`
- `Dome.Application.ShadowExtraction`
- `Dome.Cli`

### `tests`

Application, integration, contract, and support tests live here. The shared CPG package keeps its own focused suite under `src/Core/CPG/Tests`.

## Design Notes

- `src/Core/CPG` is part of the production core, not a prototype sidecar.
- `Application.Ports` defines the cross-layer contracts; adapters implement them and hosts compose them.
- Standard, runtime, and shadow extraction each keep their own context type and fixed-slot vocabulary while sharing the same recipe-based assembly model.
- `Replace<Slot>` and `Decorate<Slot>` are the only supported extension seams on the public flow-assembly surface.
- Composition roots should select recipes and bind slots, not rebuild manual stage arrays.
- The CLI stays thin and delegates orchestration to the application layer.
