# DataFlowPass Remove Immutable Intermediate State Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Remove avoidable immutable method-plan snapshots and duplicate materialization from `DataFlowPass` so giant files retain less transient memory without changing `DataFlow` edges, graph shape, or rule results.

**Architecture:** Keep the current two-stage model: worker threads analyze frozen method-local inputs, and the caller thread commits stable `DataFlow` edges in order. Replace immutable per-method plan containers with arrays and mutable read-only-by-convention dictionaries/lists, then eliminate redundant `Distinct`/`ToArray`/`ToImmutable*` conversions so each method retains only one execution copy of its flow state.

**Tech Stack:** C# / .NET 10, Roslyn `IOperation`, existing `DataFlowPass`, `BoundedPartitionWorkWindow`, xUnit, existing Terraria runtime/per-file diagnostics logs; no new dependencies.

---

## Ground Rules

- Do not change `DataFlow` or `InterproceduralDataFlow` semantics.
- Do not change default DOP, rule capability wiring, or directory pipeline behavior.
- Keep ordered commit behavior exactly as-is.
- Preserve existing telemetry fields unless a new field is required for verification.
- Use small commits per task.

## Pre-Read

- `AGENTS.md`
- `progress.md`
- `feature_list.json`
- `约束/测试代码编写教程.md`
- `docs/plans/2026-07-16-dataflowpass-remove-immutable-intermediate-state-proposal.md`
- `src/MinimalRoslynCpg/AGENTS.md`

## Baseline Commands

Run once before edits:

```powershell
pwsh -File .\init.ps1
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~PipelineComponentTests" -p:UseSharedCompilation=false
```

Expected:

- build succeeds with only known warnings
- focused tests pass before refactor starts

---

### Task 1: Lock the current `DataFlowPass` behavior with targeted regression assertions

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`

**Step 1: Add a focused regression around large method-local flow shape**

- Add or extend a test that exercises:
  - parameter seed flow
  - branch merge
  - loop back-edge
  - return-to-method-return flow
  - internal call argument-to-parameter flow
- Assert sorted `DataFlow` edges exactly, not just counts.

**Step 2: Add a regression that proves no duplicate method-local flow nodes are required**

- Build a sample with repeated references to the same operation-backed nodes.
- Assert the final `DataFlow` edges are unique and deterministic.

**Step 3: Run focused tests**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests" -p:UseSharedCompilation=false
```

Expected:

- PASS

**Step 4: Commit**

```powershell
git add tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs
git commit -m "Lock DataFlowPass behavior before plan-container cleanup"
```

---

### Task 2: Remove immutable containers from `MethodDataFlowPlan`

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`

**Step 1: Replace immutable method-plan fields with arrays and dictionaries**

- Change `MethodDataFlowPlan` fields from:
  - `ImmutableArray<IOperation>`
  - `ImmutableArray<RoslynCpgNode>`
  - `ImmutableDictionary<...>`
- To:
  - `IOperation[]`
  - `RoslynCpgNode[]`
  - `Dictionary<...>` or `IReadOnlyDictionary<...>` backed by plain dictionaries

**Step 2: Remove `NodesByLegacyId` from the plan**

- Verify commit only needs direct node references.
- Remove the field and all dependent accesses.

**Step 3: Keep the external behavior unchanged**

- Preserve method order, edge order, and telemetry values.
- Do not change candidate generation logic yet.

**Step 4: Build**

Run:

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
```

Expected:

- build succeeds

**Step 5: Run focused tests**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests" -p:UseSharedCompilation=false
```

Expected:

- PASS

**Step 6: Commit**

```powershell
git add src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs
git commit -m "Replace immutable DataFlow method plans with direct arrays and dictionaries"
```

---

### Task 3: Remove duplicate materialization while building method plans

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`

**Step 1: Collapse repeated list/array creation in `BuildCfgSensitivePartitionPlans`**

- Build `operationNodes` once.
- Build `flowNodes` once.
- Add parameter, operation, return, and exit nodes into one list, then convert once to an array.

**Step 2: Replace late `Distinct().ToArray()` with early uniqueness maintenance**

- Maintain uniqueness while assembling flow nodes.
- Do not call `Distinct()` later in `AnalyzeCfgSensitivePartition` or `CountUnreachableNodes`.

**Step 3: Replace adjacency immutable copies with array snapshots only**

- Keep `BuildFlowNeighborsFromCache(...)`.
- Output `Dictionary<RoslynCpgNode, RoslynCpgNode[]>` or equivalent once.
- Remove `ToImmutableDictionary(...ToImmutableArray())`.

**Step 4: Run build**

Run:

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
```

Expected:

- build succeeds

**Step 5: Run focused tests**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests" -p:UseSharedCompilation=false
```

Expected:

- PASS

**Step 6: Commit**

```powershell
git add src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs
git commit -m "Remove duplicate DataFlow plan materialization"
```

---

### Task 4: Shorten per-method working-set lifetime in analysis and commit

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/BoundedPartitionWorkWindow.cs`

**Step 1: Ensure candidate edge collections are released immediately after commit**

- Confirm `CfgSensitivePartition.Edges` is only held long enough for ordered commit.
- Refactor local variables so no extra references survive beyond `CommitCfgSensitivePartition(...)`.

**Step 2: Verify the ordered work window does not buffer more than required**

- Keep `RunOrdered(...)` semantics unchanged.
- Do not introduce any new all-results materialization.

**Step 3: Add or extend telemetry assertions**

- Preserve `PeakBufferedCandidateBatchCount`.
- If you add temporary counters for debugging, remove them before completion unless they are genuinely useful.

**Step 4: Run focused tests**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~PipelineComponentTests" -p:UseSharedCompilation=false
```

Expected:

- PASS

**Step 5: Commit**

```powershell
git add src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs src/MinimalRoslynCpg/Builder/BoundedPartitionWorkWindow.cs
git commit -m "Shorten DataFlowPass method-state retention"
```

---

### Task 5: Run full regression and compare real-source measurements

**Files:**
- Modify: `progress.md`
- Modify: `feature_list.json` only if this optimization becomes tracked there

**Step 1: Run full test suite**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
```

Expected:

- `391/391` tests pass
- harness consistency passes

**Step 2: Run Terraria dry-run with new log names**

Run:

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- "D:\lodes\TR\Backup\New1.27\1.45 2\TR" --delete-class PlayerInput --max-degree-of-parallelism 16 --skip-rewrite --no-diff --runtime-metrics-log .\Build\dataflow-lite-runtime-<stamp>.jsonl --per-file-memory-diagnostics-log .\Build\dataflow-lite-memory-<stamp>.jsonl --per-file-phase-timing-log-directory .\Build\dataflow-lite-phases-<stamp>
```

Expected:

- run completes successfully across the whole directory

**Step 3: Compare against the current baseline**

Compare with:

- `Build/nodeid-full-runtime-20260716-2.jsonl`
- `Build/nodeid-full-memory-20260716-2.jsonl`

Capture:

- wall-clock delta
- peak managed heap delta
- peak working set delta
- `NPC.cs` private-bytes and `cpg-build` delta
- `Player.cs` private-bytes and `cpg-build` delta
- graph node/edge equality on the peak files

**Step 4: Update handoff**

- In `progress.md`, record:
  - verification commands
  - whether memory improved
  - remaining risks
- In `feature_list.json`, only update if this optimization is tracked as part of an active feature.

**Step 5: Commit**

```powershell
git add progress.md feature_list.json
git commit -m "Record DataFlowPass transient-memory optimization results"
```

---

## Stop Conditions

- Any `DataFlow` edge diff beyond ordering noise
- Any full-suite regression
- Any increase in graph node/edge counts for the same source
- Any change that requires new rule capabilities or CLI contract changes

## Rollback Strategy

- If Task 2 causes behavior drift, revert to immutable-backed plan fields and reattempt with only `NodesByLegacyId` removal.
- If Task 3 causes behavior drift, keep direct plan containers but restore the previous adjacency snapshot shape.
- If Task 4 yields no measurable memory improvement, stop after documenting results; do not widen scope inside the same branch.

## Success Criteria

- `DataFlowPass` no longer builds immutable per-method snapshots
- full tests still pass
- giant-file logs show reduced transient memory and/or improved `cpg-build` time
- graph shape and rule outputs remain unchanged
