# Project Layout

The current `dome.sln` structure is centered on the `apps + src + tests` layout below:

```text
dome/
|-- apps/
|   |-- Dome.Application/
|   |-- Dome.Application.Runtime/
|   |-- Dome.Application.ShadowExtraction/
|   `-- Dome.Cli/
|-- docs/
|-- samples/
|-- src/
|   |-- Adapters/
|   |-- Application/
|   `-- Core/
|       `-- CPG/
`-- tests/
    |-- Dome.Tests/
    `-- TerrariaTools.Testing/
```

## Directory Responsibilities

### `apps/`

Hosts and composition roots:

- default service registration
- recipe-based fixed-slot flow selection and slot binding
- host entry points
- CLI command parsing

### `src/Core/`

Core domain packages that do not depend on adapter-specific Roslyn orchestration details:

- `Common/`: shared results, actions, diagnostics, and failures
- `Analysis/`: analysis models and query contracts
- `Planning/`: planning models
- `Planning.Services/`: plan compilation services
- `Rules/Model/`: rule decision models
- `Rules/Services/`: rule, propagation, and boundary-lift engines
- `CPG/`: schema, generated nodes, graph runtime, overlays, Roslyn frontend, and `src/Core/CPG/Tests`

### `src/Application/`

Use-case flows and public ports:

- `Ports/`: requests, results, and port interfaces
- `Pipeline/`: shared execution kernel, `FlowRecipe`, `FlowBuilder`, and standard Dome stages
- `UseCases/Runtime/`: runtime-specific stages
- `UseCases/ShadowExtraction/`: shadow-extraction-specific stages

### `src/Adapters/`

Concrete port implementations:

- workspace loaders
- Roslyn analyzers
- Roslyn rewriters
- JSON report persistence
- runtime workspace preparation and build execution

## Flow Assembly Notes

- `apps/` composition roots no longer keep manual stage arrays as the default orchestration path.
- Each application family assembles a recipe-based, fixed-slot flow and delegates execution to the shared pipeline runner.
- `src/Application/Pipeline/` owns the reusable flow-assembly primitives, while each host family keeps its own context type and slot vocabulary.

### `tests/`

Shared automated test projects:

- `Dome.Tests/`: the main application and integration test suite
- `TerrariaTools.Testing/`: test infrastructure, assertions, builders, and test doubles

### `samples/`

Small sample source trees used for focused verification:

- `closed-loop`
- `expression-loop`
- `promotion-loop`
- `protection-loop`
- `sanitization-loop`
