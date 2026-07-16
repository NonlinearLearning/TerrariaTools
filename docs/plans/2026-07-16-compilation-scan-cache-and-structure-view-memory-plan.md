# Compilation Scan Cache and Structure View Memory Optimization Plan

**Goal:** Reduce `--delete-class` peak memory in large directory runs by removing the two largest analysis-side amplifiers after `snapshotVersion`: the eager compilation-wide `CompilationScanCache` and the whole-graph `RoslynCpgStructureViewBuilder.GraphCache`.

**Scope:** This plan covers only the delete-class parameter-shrink helper scan cache and the analysis-time structure-view cache. It does not change CPG graph semantics, rule semantics, file-level atomicity, default DOP, or directory result aggregation behavior.

**Tech Stack:** .NET 10, Roslyn `Compilation` / `SemanticModel`, `ConditionalWeakTable`, `ConcurrentDictionary`, xUnit.

## Evidence and decision

The July 16, 2026 Terraria directory run with `DOP 16 --skip-rewrite --no-diff` completed after the `snapshotVersion` fix, but peak memory remained high on the largest files:

- `WorldGen.cs`: `1,914,334` CPG nodes, `4,722,378` edges, about `10.53 GiB` peak private bytes in the per-file memory log.
- `NPC.cs`: `2,108,977` CPG nodes, `5,322,409` edges, about `9.85 GiB` working set at completion.
- `Projectile.cs`: `1,716,277` CPG nodes, `4,289,900` edges, about `8.88 GiB` working set at completion.

The current `DeleteClassParameterShrinkAnalyzer` creates a compilation cache through `DeletionAnalysisRuntime.GetOrCreateCompilationCache(...)`, but the cache constructor immediately scans every syntax tree in the compilation and stores `SemanticModel` plus multiple binding indexes for each tree. The relevant eager path is in:

- `src/RoslynPrototype/RuleServices/RuleHelpers/DeleteClassParameterShrinkAnalyzer.cs:1406-1577`
- `src/RoslynPrototype/RuleServices/ExecutionRuntime.cs:172-190`

The current `RoslynCpgStructureViewBuilder` also creates a whole-graph cache per `RoslynCpgGraph` and duplicates all graph edges into `Edges = graph.Edges.ToList()`, then builds a second undirected adjacency map over the full edge set. The relevant path is in:

- `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs:52-99`
- `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs:289-311`

**Decision:** optimize `CompilationScanCache` first, then shrink `GraphCache`. The scan cache is the higher-confidence primary amplifier because it is compilation-wide, eager, and retains Roslyn semantic objects plus operation bindings across all syntax trees. The structure-view cache remains important, but it is secondary because it is triggered only when rule-scoped structure views are built.

## Compatibility constraints

- File-level analysis remains atomic. Do not aggregate or cache at function granularity across the directory.
- A given file's delete-class decisions, rewrites, diagnostics, and phase timing must remain stable.
- `DeletionAnalysisRuntime` may continue to own run-scoped caches, but no cache may eagerly materialize data for syntax trees that are never queried.
- Structure views must continue to return the same root node, node set, edge set, and shortest-connector behavior for the same fragment set.
- Existing `CacheScopeKey` invalidation semantics must remain effective for structure-view caching.
- No new dependency is permitted.

## Current design summary

### `CompilationScanCache`

`DeleteClassParameterShrinkAnalyzer` uses a compilation cache for helper queries such as:

- method invocation rewrite discovery
- element-access rewrite discovery
- delegate usage checks
- type syntax binding lookups

Today the cache constructor eagerly:

1. iterates `compilation.SyntaxTrees`
2. builds one `TreeScan` per tree
3. stores:
   - `SemanticModel`
   - all invocation bindings
   - all element access bindings
   - all expression bindings with `IOperation` and converted type
   - all type syntax bindings
4. rebuilds those lists into multiple symbol-keyed dictionaries

This means a single first cache hit can turn a 1,503-file compilation into a long-lived mirror of the entire Roslyn syntax and semantic surface.

### `RoslynCpgStructureViewBuilder.GraphCache`

`RuleContext.BuildStructureView(...)` builds rule-scoped structure views during propagate and lift. Today the graph cache eagerly:

1. copies all graph edges into a list
2. groups all graph nodes by file path
3. builds a whole-graph undirected adjacency map

The run cache then keeps per-fragment-set `RoslynCpgStructureView` instances for the full analysis context lifetime.

## Proposed design

### P1: make `CompilationScanCache` tree-lazy

Replace the eager constructor with a lazy per-tree cache.

1. Keep the compilation-scoped owner object.
2. Store the `Compilation` and a `ConcurrentDictionary<SyntaxTree, Lazy<TreeScan>>`.
3. Build a `TreeScan` only when `GetTreeScan(tree)` is called.
4. Change `GetTreeScans(...)` to enumerate trees and resolve scans lazily instead of materializing `TreeScans` up front.

Expected result: files that never participate in parameter-shrink helper queries no longer pay the scan cost or retain scan objects.

### P2: make `TreeScan` index construction demand-driven

Keep the `TreeScan` abstraction, but do not build all four binding/index families at construction time.

Split it into demand-driven sub-indexes:

- invocation index
- mapped invocation index
- element access index
- type syntax index
- expression converted-type index

`ExpressionBinding` is the most suspicious member because it holds `IOperation?` for every `ExpressionSyntax`. Delay that one last, and consider narrowing it to only the delegate-related expression shapes actually queried by the helper methods.

Expected result: helper calls that only need method-call or type-syntax lookups stop retaining unrelated expression-operation graphs.

### P3: narrow the retained Roslyn object graph

After P1 and P2 are in place, inspect whether `TreeScan.SemanticModel` still needs to be held strongly.

Preferred direction:

1. keep `SyntaxTree`
2. reacquire `SemanticModel` from `Compilation.GetSemanticModel(tree)` inside each lazy sub-index build
3. store only the final indexes, not the intermediate raw lists, unless a list is directly required by a public helper path

This is lower priority than P1/P2 because it changes object lifetimes more aggressively.

### P4: stop duplicating whole-graph structure-view data

Refactor `RoslynCpgStructureViewBuilder.GraphCache` to reuse frozen graph query surfaces instead of cloning the graph again.

Preferred direction:

1. remove `Edges = graph.Edges.ToList()`
2. reuse graph file-path lookup if available from the frozen query index
3. replace the eager whole-graph undirected adjacency map with on-demand neighbor expansion built from `GetOutgoingEdges(...)` plus `GetIncomingEdges(...)`

This trades some CPU for lower memory, which is acceptable here because the current problem is memory pressure and OOM risk.

### P5: bound structure-view result retention

Keep structure-view result caching, but stop retaining every view for the full context lifetime without a bound.

Options, in order:

1. keep the current run cache but only cache fragment sets that are requested more than once
2. cap the number of cached views per analysis context
3. if reuse is rare in practice, disable the view-object cache while keeping the lighter graph-side indexes

This phase should start only after `GraphCache` itself has been shrunk, otherwise the result is hard to measure cleanly.

## Implementation sequence

### Task 1: lock down current helper and structure-view behavior

**Files:**

- Modify: `tests/RoslynDeletionPrototype.Tests/PerformanceOptimizationRegressionTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Inspect: `src/RoslynPrototype/RuleServices/RuleHelpers/DeleteClassParameterShrinkAnalyzer.cs`
- Inspect: `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs`

1. Add regression coverage that helper-query results stay identical before and after cache refactors.
2. Add structure-view equality tests that compare node ids and edge identities for the same fragment sets.
3. Add cache-shape tests only where they verify public or stable internal behavior, not incidental implementation details.

### Task 2: implement tree-lazy `CompilationScanCache`

**Files:**

- Modify: `src/RoslynPrototype/RuleServices/RuleHelpers/DeleteClassParameterShrinkAnalyzer.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PerformanceOptimizationRegressionTests.cs`

1. Remove eager `foreach (var tree in compilation.SyntaxTrees) BuildTreeScan(...)`.
2. Store per-tree lazy entries.
3. Update `GetTreeScans(...)` and `GetTreeScan(...)` call sites to preserve existing helper semantics.
4. Verify all delete-class helper plan builders still produce identical rewrite plans.

### Task 3: implement demand-driven `TreeScan` sub-indexes

**Files:**

- Modify: `src/RoslynPrototype/RuleServices/RuleHelpers/DeleteClassParameterShrinkAnalyzer.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PerformanceOptimizationRegressionTests.cs`

1. Split raw scan data from derived indexes.
2. Build each index only on first use.
3. Delay or narrow expression-operation capture last.
4. Re-run helper-plan tests and any delete-class directory regressions that touch parameter shrink.

### Task 4: shrink `GraphCache`

**Files:**

- Modify: `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. Remove whole-edge-list duplication.
2. Reuse frozen graph lookups where possible.
3. Replace eager whole-graph undirected adjacency with on-demand neighbor traversal.
4. Verify structure views remain identical for single-fragment and multi-fragment cases.

### Task 5: measure and decide whether view-result cache bounding is needed

**Files:**

- Modify if needed: `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs`
- Test if needed: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`

1. Measure reuse frequency of identical fragment-set view requests in a representative delete-class run.
2. If reuse is low, bound or remove the per-context view-object cache.
3. If reuse is high, keep it and document the measured hit-rate justification.

## Verification commands

Run focused tests after each task:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceOptimizationRegressionTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~PipelineComponentTests"
```

For real-source validation after P2 and after P4:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- "D:\lodes\TR\Backup\New1.27\1.45 2\TR" --delete-class PlayerInput --max-degree-of-parallelism 16 --skip-rewrite --no-diff --runtime-metrics-log .\Build\terraria-dop16-runtime-<stamp>.jsonl --per-file-memory-diagnostics-log .\Build\terraria-dop16-memory-<stamp>.jsonl --per-file-phase-timing-log-directory .\Build\terraria-dop16-phases-<stamp>
```

## Acceptance criteria

1. Delete-class helper plan outputs remain identical for the covered regression fixtures.
2. Structure-view node and edge results remain identical for existing structure-view tests.
3. `CompilationScanCache` no longer scans all syntax trees on first access.
4. `GraphCache` no longer clones the full edge set into a second long-lived list.
5. The Terraria `DOP 16 --skip-rewrite --no-diff` run still completes successfully.
6. Peak managed heap and private bytes improve measurably versus the July 16, 2026 post-`snapshotVersion` baseline.

## Risks and stop conditions

| Risk | Control / stop condition |
| --- | --- |
| Lazy scan changes helper query ordering or dedup behavior | Stop on any helper-plan output diff; preserve existing symbol lookup and syntax-key semantics. |
| Reacquiring semantic models increases CPU enough to erase the memory win | Measure after P2 before proceeding to P3. |
| On-demand graph traversal changes shortest-connector results | Stop on any structure-view node/edge mismatch. |
| Removing edge duplication accidentally introduces repeated full-edge scans | Prefer frozen graph adjacency lookups over raw `graph.Edges` enumeration. |
| View-result cache removal hurts repeated rule performance materially | Measure cache hit rate before removing or capping it. |

## Out of scope

- Function-level project aggregation
- Rewrite pipeline changes
- Mark analysis snapshot redesign
- Default DOP changes
- New external storage or serialization for caches
