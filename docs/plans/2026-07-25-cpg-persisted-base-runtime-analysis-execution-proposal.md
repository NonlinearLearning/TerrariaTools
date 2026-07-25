# Persisted Base CPG Runtime Analysis Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make a CPG persistence hit return the same complete CPG as a no-persistence build by restoring only existing persisted base fragments and rebuilding all runtime-dependent analysis from current source and Roslyn semantic facts.

**Architecture:** Persistence remains a base-fragment cache, addressed through the current catalog and routing.cpgidx lookup path. A hit restores those existing fragments into a mutable graph, binds current SyntaxNode, IOperation, and symbol facts to stable NodeIds, runs the normal derived passes, and freezes once. Persisted shards never replace process-local semantic state.

**Tech Stack:** .NET 10, Roslyn, MinimalRoslynCpg, SQLite, binary shard store, xUnit

---

## Scope and immovable constraints

### Required result

For identical current input and compatible profile, all three builds must produce one exact final graph:

1. No-persistence baseline.
2. Cold build that writes persistence.
3. Subsequent persisted-base hit.

“Exact” means node set, edge set, GraphSnapshotVersion, and slice/rule behavior all match. An edge comparison includes source NodeId, target NodeId, kind, stable label, context id, and every call-site-context field. Counts or snapshot hash alone are insufficient.

### Storage contract

- Preserve the current basic-fragment positioning plus catalog/routing-sidecar lookup model.
- Continue using only existing roles: file-skeleton, method-boundary fragments, operation-fragment, and existing boundary-adjacency/cross-shard edge records.
- Do **not** add a field, record, companion payload, or new shard role to every fragment.
- Do **not** add a complete final-graph snapshot, derived-edge delta, or compensation shard.
- Do **not** persist SemanticModel, SyntaxNode, IOperation, CFG, data-flow facts, dominance, control-dependence, or process-local analysis state.
- Retain completed-build visibility, legacy catalog fallback, Strict validation, catalog publication, and missing/corrupt-store fallback.

### Non-goals

- Optimizing disk size, catalog time, routing-sidecar encoding, or default DOP.
- Treating the formerly faster but graph-inequivalent DOP12 restore result as performance evidence.
- Making derived analysis durable across processes.

## Target execution path

~~~text
current source + current Roslyn SemanticModel
  -> existing catalog/routing lookup
  -> read and validate existing base shards
  -> mutable base graph
  -> runtime binding by stable NodeId
  -> CallGraph / MemberAccess / CFG / DataFlow /
     InterproceduralDataFlow / Dominance / ControlDependence
  -> FreezeQueryIndex
  -> full CPG equivalent to no-persistence build
~~~

On an absent, incomplete, corrupt, incompatible, or unbindable base result, discard it and run the cold path. Never mix partial restored data with a cold-build graph.

## Baseline files

| File | Current responsibility | Planned responsibility |
| --- | --- | --- |
| src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs | Early return after TryRestoreAsync | Orchestrate restore, binding, dynamic passes, final freeze, fallback |
| src/MinimalRoslynCpg/Builder/CpgShardBuildCoordinator.cs | Catalog/routing lookup and frozen reconstruction | Return validated base restoration only |
| src/MinimalRoslynCpg/Persistence/CpgFrozenShardGraphReader.cs | Read shard records | Populate existing base facts into mutable reconstruction |
| src/MinimalRoslynCpg/Builder/RoslynCpgBuildContext.cs | Current source/root/model/graph/inventory | Supply current runtime semantic state for binding |
| src/MinimalRoslynCpg/Builder/Passes/*.cs | Build derived facts | Remain the sole source of derived facts on a hit |
| tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs | Restore and DOP tests | Exact equivalence, fallback, and no-growth contracts |

## Tasks

### Task 1: Lock the correctness contract

**Files:**

- Modify: tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs
- Modify: tests/RoslynDeletionPrototype.ContractTests/Cpg/RoslynCpgSliceQueryTests.cs
- Modify only if its helper is insufficient: tests/RoslynDeletionPrototype.ContractTests/Cpg/MinimalRoslynCpgPartitionedBuilderTests.cs

**Step 1: Write a failing exact-graph hit test.**

Use a source fixture with call, member access, branch/loop, and interprocedural flow. Build a no-persistence baseline; seed a Strict streaming store; then build again using the same persistence options. Add local helpers that project, sort, and compare all node identity fields and all edge fields.

~~~csharp
Assert.Equal(ExactNodes(baseline), ExactNodes(restored));
Assert.Equal(ExactEdges(baseline), ExactEdges(restored));
Assert.Equal(baseline.GraphSnapshotVersion, restored.GraphSnapshotVersion);
Assert.Contains("DataFlowPass", hitBuilder.LastBuildTelemetry.ExecutedPassNames!);
Assert.NotEmpty(hitBuilder.LastBuildTelemetry.ExecutedPassNames!);
~~~

**Step 2: Verify current behavior is red.**

Run:

~~~powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardBuildCoordinatorTests.BuildFromSource_PersistenceHit_RebuildsCompleteGraph"
~~~

Expected: FAIL. The current hit has an empty ExecutedPassNames list and/or different exact edges because it returns before dynamic passes.

**Step 3: Add DOP and query cases.**

Use Theory DOP values 1 and 12. For each, compare baseline, cold seed, and hit. Query a backward slice from a known data-flow target and compare ordered path NodeIds. Add a rule-facing assertion through an existing fixture only if slice equivalence does not cover the regression.

**Step 4: Run the red set.**

~~~powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~RoslynCpgSliceQueryTests"
~~~

Expected: new cases fail; existing cases stay green.

**Step 5: Commit.**

~~~powershell
git add tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs tests/RoslynDeletionPrototype.ContractTests/Cpg/RoslynCpgSliceQueryTests.cs
git commit -m "Lock persisted CPG hit equivalence contract"
~~~

### Task 2: Separate base restoration from final-graph restoration

**Files:**

- Modify: src/MinimalRoslynCpg/Builder/CpgShardBuildCoordinator.cs
- Modify: src/MinimalRoslynCpg/Persistence/CpgFrozenShardGraphReader.cs
- Modify only if an in-memory type needs a contract: src/MinimalRoslynCpg/Persistence/CpgShardContracts.cs
- Test: tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs

**Step 1: Define an internal base-restore result.**

Replace the plan-level meaning of TryRestoreAsync with TryRestoreBaseAsync (or equivalent). It says validated base fragments were found and exposes mutable reconstruction input; it must not claim to be a frozen final graph.

~~~csharp
internal sealed record CpgBaseRestoreResult(
    RoslynCpgGraph Graph,
    CpgPersistenceTelemetry PersistenceTelemetry);
~~~

The exact shape may differ, but it stays internal and is never serialized.

**Step 2: Preserve lookup and validation.**

Keep FindByFileAsync, routing-sidecar resolution, shard reads, completed-build visibility, and corruption handling. The reader may populate a mutable graph but must read exactly the existing records and add no schema field or role.

**Step 3: Add a failing cache-fallback test.**

Create a completed store, then simulate missing base fragment, invalid shard bytes, or NodeId mapping mismatch. Assert the builder discards the candidate and produces the no-persistence graph exactly; it must not return partial data or throw for recoverable cache-read failure.

**Step 4: Implement the minimal reader change.**

Restore only existing owned node/edge facts and boundary-adjacency records. Preserve deterministic NodeIds and reject duplicate/conflicting ownership. Do not freeze before runtime binding.

**Step 5: Run restore/catalog contracts.**

~~~powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~SqliteCpgShardCatalogTests"
~~~

Expected: existing completed-session/routing/catalog tests and new fallback test pass.

**Step 6: Commit.**

~~~powershell
git add src/MinimalRoslynCpg/Builder/CpgShardBuildCoordinator.cs src/MinimalRoslynCpg/Persistence/CpgFrozenShardGraphReader.cs src/MinimalRoslynCpg/Persistence/CpgShardContracts.cs tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs
git commit -m "Restore persisted CPG fragments as a mutable base"
~~~

### Task 3: Bind current Roslyn facts to restored identities

**Files:**

- Create: src/MinimalRoslynCpg/Builder/Persistence/RoslynCpgRuntimeBindingPass.cs
- Modify: src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs
- Modify only for minimal internal accessors: src/MinimalRoslynCpg/Builder/RoslynCpgBuildContext.cs
- Test: tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs

**Step 1: Write failing binding tests.**

After a base hit, prove representative syntax, operation, declaration/method, symbol, and call-site identities resolve to the same NodeIds as baseline. Include overload resolution and member access so syntax-only binding cannot pass.

**Step 2: Implement runtime-only binding.**

RoslynCpgRuntimeBindingPass walks the *current* context.Root with the *current* context.SemanticModel and reconstructs the maps needed by current passes:

- SyntaxNode -> RoslynCpgNode
- IOperation -> RoslynCpgNode
- symbol key -> RoslynCpgNode
- method/declaration and call-site mappings
- OperationInventory entries used by CFG and data-flow

Resolve with cold-build stable inputs: file path, source span, syntax/operation kind, symbol identity, and preallocated NodeId where applicable. If a mapping is missing, ambiguous, or inconsistent, reject the whole hit and cold-build. Binding creates no second basic node or structural edge.

**Step 3: Remove the semantic early return.**

In RoslynCpgBuilder.Build, replace the current TryRestoreAsync early return with:

~~~csharp
var baseRestore = coordinator.TryRestoreBaseAsync(context, CancellationToken.None)
    .GetAwaiter().GetResult();
if (baseRestore is not null && TryBindCurrentRuntimeState(context, baseRestore))
{
    // Continue through dynamic passes and final freeze.
}
else
{
    // Discard candidate state and use current cold construction.
}
~~~

Do not rerun SyntaxPass, MethodDecorationPass, or OperationPass over restored base facts unless proven not to duplicate nodes/structural edges. Prefer the binding pass and current identity helpers.

**Step 4: Add in-memory telemetry.**

Add runtime-only telemetry: PersistenceHit, BaseRestoreElapsedMilliseconds, RuntimeBindingElapsedMilliseconds, and PersistenceFallbackReason. It is diagnostic data, never persisted. A hit lists dynamic passes but does not falsely claim a cold syntax/operation build.

**Step 5: Run identity contracts.**

~~~powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardBuildCoordinatorTests.BuildFromSource_PersistenceHit|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgNodeIdContractTests"
~~~

Expected: exact hit equality, binding coverage, and deterministic identity contracts pass.

**Step 6: Commit.**

~~~powershell
git add src/MinimalRoslynCpg/Builder/Persistence/RoslynCpgRuntimeBindingPass.cs src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs src/MinimalRoslynCpg/Builder/RoslynCpgBuildContext.cs tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs
git commit -m "Bind current Roslyn state to restored CPG bases"
~~~

### Task 4: Rebuild derived analysis dynamically and freeze afterward

**Files:**

- Modify: src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs
- Modify only if a documented recovered-state seam is required: src/MinimalRoslynCpg/Builder/Passes/ControlFlowPass.cs, DataFlowPass.cs, InterproceduralDataFlowPass.cs, PartitionedOperationPass.cs, or PartitionedSyntaxPass.cs
- Test: tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs
- Test: tests/RoslynDeletionPrototype.ContractTests/Cpg/RoslynCpgSliceQueryTests.cs

**Step 1: Add failing derived-edge matrix assertions.**

For a source with branch, loop, invocation, member access, and cross-method flow, compare exact CfgNext, DataFlow, call, member-access, dominance, and control-dependence edge sets under each capability. This guards the known lost boundary-adjacency shape.

**Step 2: Route hits through existing passes.**

After binding, use the same ResolveCapabilityBuildPlan gates as the cold path for CallGraphPass, MemberAccessPass, ControlFlowPass, DataFlowPass, InterproceduralDataFlowPass, DominancePass, and ControlDependencePass. Derived facts always come from current semantic facts; none are read from new durable data.

**Step 3: Enforce lifecycle.**

Call FreezeQueryIndex exactly once after dynamic passes. Call ReleaseTransientBuilderState only after final freeze. Fallback cleanup clears partial maps before cold construction begins.

**Step 4: Avoid republishing validated hits.**

Preferred behavior is no write on an unchanged complete hit. If present lifecycle requires publication, make it reuse physical fragments without new data. Assert same-input hit does not increase shard/catalog/routing inventory.

**Step 5: Run pass and slice tests.**

~~~powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests"
~~~

Expected: exact edges, slices, snapshots, and capability telemetry pass for cold and hit builds.

**Step 6: Commit.**

~~~powershell
git add src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs src/MinimalRoslynCpg/Builder/Passes tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs tests/RoslynDeletionPrototype.ContractTests/Cpg/RoslynCpgSliceQueryTests.cs
git commit -m "Rebuild CPG derived analysis after base restoration"
~~~

### Task 5: Prove fallback isolation and storage invariants

**Files:**

- Modify: tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs
- Modify only if assertions are absent: tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs
- Modify telemetry surface only if needed: tests/RoslynDeletionPrototype.PerformanceTests/Cpg/CpgPersistenceBenchmarkConfigurationTests.cs, tools/CpgPersistenceBenchmark/BenchmarkConfiguration.cs, tools/CpgPersistenceBenchmark/Program.cs

**Step 1: Write failing non-growth tests.**

After seed, capture shard names/lengths, catalog counts, routing-sidecar length/hash, and roles. Run a same-input hit. Assert no new role, schema change, or per-fragment payload. If current publication requires session metadata, document and report it separately.

**Step 2: Cover the fallback matrix.**

Test profile/schema/input mismatch, missing location, corrupt shard, and invalid binding. Every case proves no partial return, exact cold-graph equality, meaningful fallback telemetry, and query-index completion.

**Step 3: Implement one narrow discard path.**

Dispose/discard candidate graph and recovered maps together. Only cache-read/bind failures fall back; cold-build failures remain visible.

**Step 4: Run integrity contracts.**

~~~powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~SqliteCpgShardCatalogTests|FullyQualifiedName~CpgFrozen"
~~~

Expected: fallback, non-growth, Strict, catalog, and reader contracts pass.

**Step 5: Commit.**

~~~powershell
git add tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs tests/RoslynDeletionPrototype.PerformanceTests/Cpg/CpgPersistenceBenchmarkConfigurationTests.cs tools/CpgPersistenceBenchmark/BenchmarkConfiguration.cs tools/CpgPersistenceBenchmark/Program.cs
git commit -m "Guard persisted CPG hits against storage growth"
~~~

### Task 6: Run complete correctness and performance verification

**Files:**

- No production changes expected.
- Capture results under artifacts/cpg-persisted-base-runtime-analysis-<timestamp>/.

**Step 1: Build and run full contract tests.**

~~~powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
~~~

Expected: build succeeds and contract project passes.

**Step 2: Run DOP matrix.**

At DOP 1 and 12, run baseline, cold seed, and hit on the same small fixture. Persist exact graph projections, snapshot, pass list, baseRestoreMs, runtimeBindingMs, dynamic pass times, and fallback reason. Any exact-set difference fails the matrix.

**Step 3: Run rule and slice equivalence.**

Use existing slice/query and deletion-rule fixtures to compare slice paths plus final rule/decision output between baseline and hit. Build success alone is not semantic evidence.

**Step 4: Run bounded real-source DOP12 comparison.**

Use the 103-file fixture with DOP12. Seed outside timed reuse samples, then take at least two same-input hit samples and a no-persistence comparison. Retain runtime/analysis logs and exact graph summaries. Report cold seed separately. Missing completion, mismatch, OOM, disk failure, or fallback invalidates a sample.

**Step 5: Validate the storage invariant.**

Compare seed vs hit inventory and bytes for shards, routing.cpgidx, and catalog.db. Acceptance is no new per-shard payload/role and no unexplained hit-time growth. This is not a disk-reduction target.

**Step 6: Run checks.**

~~~powershell
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
~~~

Expected: both pass without whitespace errors.

### Task 7: Record status only after verification

**Files:**

- Modify after implementation only: progress.md
- Modify after implementation only: feature_list.json
- Optional evidence note: docs/plans or artifacts; do not rewrite this proposal as a completion report.

**Step 1: Update authoritative state.**

Update feature_list.json only from verified evidence. Keep progress.md concise: feature id, verified boundary, commands/results, remaining risks, and next action.

**Step 2: Record gaps honestly.**

Report any absent 1,503-file proof, cross-DOP variance, or untested migration behavior. Do not describe mismatch/fallback as a reuse performance result.

**Step 3: Commit.**

~~~powershell
git add progress.md feature_list.json docs/plans artifacts
git commit -m "Record persisted base CPG analysis verification"
~~~

## Acceptance gates

| Gate | Required evidence |
| --- | --- |
| Exact graph | Node/edge projections equal for baseline, seed, hit; snapshots equal |
| Dynamic reconstruction | Hit telemetry has base restore, binding, dynamic passes; pass list not empty |
| DOP | Exact equality at DOP1 and DOP12; no automatic default change |
| Runtime behavior | Slice/query and selected rule/decision output equal |
| Cache failure | Missing/corrupt/incompatible/unbindable cache atomically cold-builds |
| Storage boundary | Existing roles/schema only; no per-shard addition, snapshot, compensation |
| Durability | Strict, completed visibility, routing lookup, legacy fallback remain green |
| Benchmark validity | Seed separate from reuse; only graph-equivalent completed samples reported |

## Risks and decisions

- **Identity ambiguity:** reject the cache hit; never duplicate basic nodes heuristically.
- **Pass preconditions:** reconstruct builder-private runtime maps, not shard payload.
- **Duplicate facts:** red tests must prove binding does not re-add base construction facts.
- **Telemetry:** report base read, binding, and dynamic analysis separately.
- **Performance:** correctness first; optimize only after timings identify a real hotspot.

## Definition of complete

Implementation is complete only when every acceptance gate passes and retained evidence proves a persisted-base hit returns a graph-equivalent full CPG without adding data to each existing shard. Until then, persistence is partial/base restoration, not a substitute for a complete analysis build.

