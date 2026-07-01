# Minimal Roslyn CPG Code Layout

## Directory Layout

```text
src/MinimalRoslynCpg/
  MinimalRoslynCpg.csproj
  Program.cs
  Cli/
    MinimalRoslynCpgCli.cs
  Contracts/
    RoslynCpgNodeKind.cs
    RoslynCpgEdgeKind.cs
    RoslynCpgViewDirection.cs
  Model/
    RoslynCpgNode.cs
    RoslynCpgEdge.cs
    RoslynCpgGraph.cs
    RoslynCpgLocalView.cs
  Builder/
    RoslynCpgBuilder.cs
  docs/
    node-edge-catalog.md
    code-layout.md
```

## First Batch Classes

### `Cli/MinimalRoslynCpgCli.cs`
Thin CLI surface that keeps default graph stats output and adds node-anchor local-view extraction.

### `Contracts/RoslynCpgNodeKind.cs`
Defines the minimal Roslyn-native node taxonomy.

### `Contracts/RoslynCpgEdgeKind.cs`
Defines syntax, semantic, operation, and minimal analysis edges.

### `Contracts/RoslynCpgViewDirection.cs`
Defines local-view traversal direction for node-anchor expansion.

### `Model/RoslynCpgNode.cs`
Single immutable node record for all graph layers.

### `Model/RoslynCpgEdge.cs`
Single immutable edge record.

### `Model/RoslynCpgGraph.cs`
Minimal graph storage with deduplicated nodes and edges, plus node-anchor local-view extraction.

### `Model/RoslynCpgLocalView.cs`
Immutable result payload for a local node-anchor subgraph view.

### `Builder/RoslynCpgBuilder.cs`
Main builder that:

1. parses source with Roslyn
2. emits syntax nodes and tokens
3. emits declared and referenced symbols
4. emits type declaration, type reference, reference, and call-site abstractions
5. emits operation trees
6. links syntax, symbol, type, and operation layers
7. emits operation-based method-local CFG
8. emits intraprocedural local/parameter reaching-definition style data flow

### `Program.cs`
Tiny executable entrypoint that delegates to the CLI surface.

## Deliberate Omissions

This first batch does not yet include:

- project/workspace loading
- external dependency summary ingestion
- persisted serialization format
- full control-flow graph builder
- full def-use transfer semantics
- query DSL

These should be layered after the Roslyn-native fact graph is stable.
