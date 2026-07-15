# Stable Graph Commit Parallelization Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Reduce CPG wall-clock time by moving safe SyntaxPass and DataFlow calculations into bounded read-only workers while preserving a single deterministic owner for graph mutation and builder caches.

**Architecture:** Each pass has three phases: the caller serially freezes node IDs and required lookup maps; a bounded worker window produces immutable ordinal candidate batches; one caller-thread committer consumes batches in source order and owns `RoslynCpgGraph`, sequence counters, and all builder dictionaries. The first implementation keeps strict source-order commit. It does not make `RoslynCpgGraph` thread-safe and does not change the default DOP.

**Tech Stack:** C# / .NET 10, Roslyn `SemanticModel` / `IOperation`, `Task`, existing `BoundedPartitionWorkWindow`, `ArrayPool<T>`, xUnit.

---

## Baseline evidence

- The full Terraria DOP 16 dry run completed in `292.5s`; cumulative CPG time was `876389ms`, and `Projectile.cs` was the largest CPG file at `98701ms`. These are per-file accumulated durations, not wall-clock.
- WorldGen CPG sampling retained identical `1785776` nodes and `4155154` edges at DOP 1, 16, and 64. DOP 16 reduced wall-clock from `36668ms` to `30270ms`; DOP 64 was `30493ms`. Raising DOP is not the remaining lever.
- `PartitionedSyntaxPass` already reads semantic facts in workers, then calls the complete serial `RunLegacySyntaxPass`. `SyntaxPass` mutates the graph, `_syntaxNodes`, pending type sets, and symbol/type maps.
- `DataFlowPass` already runs UsedFacts and CFG-sensitive fixpoints in bounded method-local workers. It still serially prepares graph-backed plans and emits value-source, return, and terminal edges.

## Compatibility and safety constraints

- `RoslynCpgGraph` remains single-writer. Do not add locks or concurrent collections to `_nodes` / `_edges`.
- A worker may read `SemanticModel`, syntax, operations, and immutable snapshots only. It must not call a `GetOrCreate*` helper, mutate a builder collection, increment a sequence, or retain a pooled buffer after returning.
- Preserve current node IDs, edge identities, node/edge sets, CPG telemetry meaning, and deterministic exported output at DOP 1, 8, 12, 14, and 16.
- Keep `RoslynCpgBuilder` single-use and single-build-at-a-time.
- Candidate batches use stable `(ShardOrder, LocalOrder)` ordering. The committer adds every node in a batch before its edges.
- Bounded worker count alone is insufficient for large files. Candidate storage must be chunked and released immediately after ordered commit.
- Do not introduce a new package or a dedicated thread pool.

## Candidate and commit contract

Add internal value records near the builder passes; do not expose them from the public graph model.

```csharp
private sealed record NodeCandidate(int LocalOrder, RoslynCpgNode Node);

private sealed record EdgeCandidate(
  int LocalOrder,
  string SourceId,
  string TargetId,
  RoslynCpgEdgeKind Kind,
  string? Label = null);

private sealed record GraphCandidateBatch(
  int ShardOrder,
  IReadOnlyList<NodeCandidate> Nodes,
  IReadOnlyList<EdgeCandidate> Edges);
```

The committer resolves IDs only from its own graph/cache state. Workers therefore reference a node by stable ID, never by a mutable `RoslynCpgNode` cache lookup. A batch cannot carry `ArrayPool<T>` arrays across the boundary; convert a full chunk to an owned array before publishing, then return worker-local buffers in `finally`.

## Task 1: Add deterministic graph-snapshot and commit-boundary regressions

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`

**Step 1: Write complete graph snapshot helpers**

Add test-local helpers that sort nodes by `Id` and edges by source ID, kind, target ID, and label. Compare the resulting sequences, not collection enumeration order.

**Step 2: Write failing behavioral tests**

Use a fixture containing multiple methods, nested control flow, returns, method calls, and shared symbols. Add tests that require complete node and edge equality for DOP 1, 8, 12, 14, and 16 across repeated builds.

**Step 3: Add a test-only commit observer seam**

Add an internal observer or callback used only by tests to record `(ShardOrder, LocalOrder)` as a batch commits. It must observe public graph results; it must not assert private dictionary implementation details.

**Step 4: Verify red then green**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests
```

Expected before later tasks: new candidate-commit assertions fail because no candidate committer exists. Retain the complete graph tests as the regression gate for every subsequent task.

## Task 2: Introduce a bounded ordered candidate committer

**Files:**
- Create: `src/MinimalRoslynCpg/Builder/OrderedGraphCandidateCommitter.cs`
- Modify: `src/MinimalRoslynCpg/Builder/BoundedPartitionWorkWindow.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

**Step 1: Write failing ordering and release tests**

Use controllable shards that complete out of source order. Assert that the committer writes only the next ordinal batch, commits its nodes before edges, and drops the completed batch reference immediately after commit.

**Step 2: Implement the smallest ordered merge API**

Keep at most the existing DOP worker window plus a bounded out-of-order result buffer. Expose a callback that receives a completed `GraphCandidateBatch` only in increasing `ShardOrder` order. Clear the result slot after callback return.

**Step 3: Add cancellation and failure behavior**

When a worker fails or cancellation is requested, stop scheduling new work, await already-started workers, and do not publish a partial graph as a successful result.

**Step 4: Verify green**

Run the focused partition tests. Verify that completion order differs in the probe but commit order remains stable.

## Task 3: Convert DataFlow ordinary-edge collection first

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

**Step 1: Freeze DataFlow node maps serially**

Before workers start, build immutable method-local snapshots containing operation-to-node IDs, parameter/return/exit node IDs, ordered operations, and CFG neighbors. Continue to create every operation and method node on the caller thread because operation IDs use `_operationSequence`.

**Step 2: Write failing DataFlow equivalence tests**

Cover value-source edges, explicit return flow, implicit terminal flow, parameter flow, and a method whose source operation is in a different nested expression. Assert exact DataFlow edge equality between DOP 1 and 16.

**Step 3: Generate only edge candidates in workers**

Move value-source, return, and terminal edge discovery into method-local workers. Workers emit `EdgeCandidate` records using frozen IDs. Keep cross-method call/argument flow serial in this task.

**Step 4: Commit in method/source order**

Use the ordered committer to add candidate edges. Preserve the existing CFG-sensitive plan/fixpoint path and its telemetry; add separate candidate-generation and commit timings rather than reusing cumulative worker time as wall-clock.

**Step 5: Verify green**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~DataFlow"
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore -p:UseSharedCompilation=false
```

## Task 4: Split SyntaxPass into serial skeleton, candidate generation, and commit

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/SyntaxPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

**Step 1: Define a serial skeleton boundary**

Serially enumerate the syntax tree in current preorder and assign each method-body shard a stable ordinal. Keep nodes outside method bodies on the existing serial path. Retain the current `SyntaxId(...)` and `SymbolId(...)` formats.

**Step 2: Write failing syntax parity tests**

Use nested methods, constructors, accessors, local functions, declaration nodes, references, type references, and tokens. Assert exact node and edge snapshots plus declared-symbol/type telemetry invariants across DOP 1 and 16.

**Step 3: Produce complete method-body candidate batches**

Each worker consumes the already-computed semantic facts and emits syntax nodes, token nodes, symbol/type node descriptors, and all local edges. It must also emit symbol-container/base-type/return-type closure descriptors required by the existing helper semantics.

**Step 4: Commit batches with the original traversal order**

The caller-thread committer inserts nodes then edges and updates `_syntaxNodes`, `_symbolNodes`, `_pendingOperationSyntaxTypeNodes`, and related counters. Remove the second full method-body traversal from `RunLegacySyntaxPass`; retain it for non-partitioned syntax only.

**Step 5: Verify green**

Run the focused builder suite at DOP 1, 8, 12, 14, and 16. Repeat the fixture ten times at DOP 16 to detect nondeterminism.

## Task 5: Measure before changing defaults

**Files:**
- Modify: `progress.md`
- Modify: `feature_list.json`
- Modify: `docs/quick-start.md`

**Step 1: Capture pass-level evidence**

Record serial candidate-generation time, ordered-commit time, candidate counts, peak buffered batches, and total CPG wall-clock. Keep aggregate worker CPU time labeled as cumulative, never wall-clock.

**Step 2: Run accepted DOP configurations**

When the Terraria source directory is available, run three isolated `--skip-rewrite --no-diff` measurements for DOP 8, 12, 14, and 16 using the same source and logging configuration.

**Step 3: Decide whether to keep each pass**

Retain a parallel candidate path only when all graph-equivalence tests pass and median CPG wall-clock does not regress against the current partitioned baseline. Leave the default DOP unchanged unless the existing threadpool/memory proposal gates are also met.

## Acceptance criteria

1. No worker directly writes `RoslynCpgGraph` or any builder cache.
2. DOP 1, 8, 12, 14, and 16 produce identical sorted node and edge snapshots.
3. DataFlow candidate collection preserves value-source, return, terminal, parameter, and CFG-sensitive edges.
4. Partitioned SyntaxPass no longer re-traverses method-body syntax solely to materialize facts already represented in a committed candidate batch.
5. The result buffer is bounded, completed batches are released after commit, and pooled worker buffers are returned in `finally`.
6. Telemetry distinguishes candidate computation, ordered commit, and cumulative worker CPU duration.
7. No default DOP or public graph schema changes occur without repeated real-source measurements.

## Risks and stop conditions

| Risk | Control / stop condition |
| --- | --- |
| Operation/call-site IDs depend on serial sequences | Freeze or create those nodes before workers; stop on any snapshot mismatch. |
| Symbol closure misses an existing recursive helper side effect | Add a focused shape fixture; stop if legacy and candidate graphs differ. |
| A large early shard blocks ordered commit and raises memory | Use bounded chunks and record peak buffered batches; stop if memory regresses. |
| More workers increase CPU contention | Compare median wall-clock at accepted DOP values; do not raise the default DOP. |
| Graph insertion dominates after candidate generation | Keep DataFlow-only result if SyntaxPass measurement does not improve; do not add concurrent graph writers. |
