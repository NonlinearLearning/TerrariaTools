# Concurrency Pool Safety, Backpressure, And Scheduling Cost Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Make `NL.Concurrency` fail promptly and deterministically, bound retained completed work, and remove avoidable scheduler allocations without changing rule results, CPG graph output, CLI DOP defaults, or resource ownership.

**Architecture:** `NL.Concurrency` remains a BCL-only bounded work dispatcher over the CLR `ThreadPool`. Ordered CPG materialization remains on the stable calling thread, `CpgBuildAdmissionBudget` remains the CPG CPU admission authority, and existing bounded `Channel` pipelines remain the persistence backpressure boundary. The work adds only scheduling contracts: a valid minimum ordered buffer, linked cancellation for one dependency-graph run, a no-result foreach worker loop, and a single-submission CPU-bound ordered path.

**Tech Stack:** .NET 10, `Task`, `CancellationTokenSource`, `TaskCompletionSource`, xUnit, existing CPG contract tests, existing HostTests, and `dotnet-counters` or the existing runtime metrics log for real-source measurements.

---

## Scope And Non-Goals

### In Scope

- Define the public behavior of `ConcurrencyWindowOptions.ReorderAllowance == 0` and prevent ordered APIs from stopping before any work starts.
- Bound both completed collection and completed result buffers in `CommitTwoStageOrdered` with one shared work-item window plus the existing retained-record limit.
- Cancel still-running DAG work after the first failure, wait for that work to observe cancellation, and rethrow the first failure.
- Add a synchronous CPU-bound ordered-selection path with one ThreadPool submission per active worker.
- Make `ForEachAsync` execute with bounded workers without allocating the discarded `TResult[]` used by `SelectOrderedAsync`.
- Add operation telemetry sufficient to compare queueing, retention, cancellation drain, and scheduling cost before changing DOP.

### Explicit Non-Goals

- Do not change `--max-degree-of-parallelism`, `--cpg-max-degree-of-parallelism`, or their defaults.
- Do not call `ThreadPool.SetMinThreads`, add a custom CLR scheduler, add a dependency, or introduce automatic DOP tuning.
- Do not replace `CpgBuildAdmissionBudget`, bounded `Channel` writers, `SemaphoreSlim` reorder slots, SQLite batching, or cross-process store locks.
- Do not relax source-order commit, DOP graph equivalence, stable node IDs, rule-graph ready order, or first-observed failure semantics.

## Required Invariants

1. Every ordered operation with non-empty input schedules at least one item, including `ReorderAllowance == 0`.
2. A completed item is never committed before all preceding source indexes commit.
3. `CommitTwoStageOrdered` cannot retain more completed work items than its shared ordered buffer capacity, and cannot retain more logical records than `MaxCompletedRecordCount` permits.
4. A DAG failure cancels sibling work through a run-local token. The original exception is rethrown only after started work has completed or observed cancellation.
5. `SelectOrderedAsync` preserves its current compatibility guarantee for delegates that block before their first `await`.
6. CPU-bound callers use a new one-submission worker path; truly async callers remain on the existing async path.
7. `ForEachAsync` preserves bounded concurrency, cancellation, and first-failure behavior while retaining no source-sized result array.
8. All production semantics at DOP 1, 8, 12, 14, and 16 remain equivalent to the DOP 1 graph/rule baseline.

## Phase 0: Establish Baselines And Test Harness

### Task 1: Record the current focused baseline

**Files:**

- Read: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Read: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/ConcurrencyPoolContractTests.cs`
- Read: `tests/RoslynDeletionPrototype.HostTests/Application/RuleGraphCompilerTests.cs`
- Read: `tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**Step 1: Run the existing concurrency contracts without changing code.**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore --filter FullyQualifiedName~ConcurrencyPoolContractTests
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore --filter FullyQualifiedName~RuleGraphCompilerTests
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore --filter FullyQualifiedName~NLCPGPartitionedBuilderTests
```

Expected: all selected tests pass. Run commands sequentially because projects share build outputs.

**Step 2: Capture a bounded real-source diagnostic sample.**

Use an existing safe input and existing runtime metrics logging. Record source identity, warmup policy, DOP, elapsed time, allocation, heap, ThreadPool counters, CPG admission wait, directory publication wait, and graph size. Do not treat a cancelled, out-of-memory, or resource-exhausted run as a benchmark.

**Acceptance gate:** The implementation work starts only with a passing focused semantic baseline or a separately documented pre-existing harness failure.

## Phase 1: Fix Ordered-Window Safety And Retention Bounds

### Task 2: Lock the zero-reorder contract with failing tests

**Files:**

- Modify: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/ConcurrencyPoolContractTests.cs`
- Modify: `tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**Step 1: Add a public-behavior test for `CommitOrdered` with `ReorderAllowance: 0`.**

Arrange source index 0 to block and index 1 to finish first at DOP 2. Release index 0 and assert that the method completes, starts work, and commits `[0, 1]` in source order. The test must use `TaskCompletionSource` with `RunContinuationsAsynchronously` and a finite test timeout.

**Step 2: Add the equivalent two-stage test.**

Make collect for index 1 finish before index 0. Assert that `prepare` and `commit` remain source ordered and no "stopped before all results were committed" exception occurs.

**Step 3: Add a shared-buffer test for `CommitTwoStageOrdered`.**

Use a blocked first source and later completing sources. Record the maximum of `completedCollections.Count + completedResults.Count` through test instrumentation exposed by a result callback or small internal diagnostic hook. Assert it never exceeds the configured effective buffer capacity. Do not inspect private fields.

**Step 4: Run the tests and confirm the first two fail against the current implementation.**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore --filter "FullyQualifiedName~CommitOrdered_WhenReorderAllowanceIsZero|FullyQualifiedName~CommitTwoStageOrdered_WhenReorderAllowanceIsZero"
```

Expected: failure caused by the current ordered window scheduling no task when the effective allowance is zero.

### Task 3: Implement a non-zero operational buffer and shared two-stage bound

**Files:**

- Modify: `src/NL.Concurrency/ConcurrencyWindowOptions.cs`
- Modify: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Modify: `src/NL.Concurrency/IConcurrencyPool.cs` only if a public diagnostic result is necessary for the Task 2 behavior test

**Step 1: Define the public meaning of zero.**

Keep `ReorderAllowance` non-negative. Change the effective operational buffer to at least one completed work item and document that zero means "do not schedule additional work after the compulsory head-unblocking completed item is buffered." Do not silently reinterpret a negative value; continue normalizing only according to the existing public contract or explicitly reject negatives if all callers already validate them.

**Step 2: Use the effective buffer in both ordered APIs.**

Replace the direct `completedResults.Count < EffectiveReorderAllowance` guards in `CommitOrdered` and `CommitTwoStageOrdered`. In the two-stage method, use one combined completed-work-item count for collections and solved results, while retaining the existing aggregate `completedRecordCount` guard.

**Step 3: Preserve progress at the head.**

When source index `nextOrderToPrepare` or `nextOrderToCommit` is already buffered, allow its serial preparation or commit even while no new parallel work may enter the window. Keep cancellation checks before every externally visible callback.

**Step 4: Run focused tests.**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore --filter FullyQualifiedName~ConcurrencyPoolContractTests
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore --filter FullyQualifiedName~NLCPGPartitionedBuilderTests
```

Expected: zero-reorder, source-order, completed-record, and DOP-equivalence assertions pass.

**Step 5: Commit.**

```powershell
git add src/NL.Concurrency tests/RoslynDeletionPrototype.PerformanceTests/Concurrency tests/RoslynDeletionPrototype.ContractTests/Cpg
git commit -m "Prevent ordered windows from stalling at zero reorder allowance"
```

**Acceptance gate:** Ordered work has a documented minimum operational buffer and a provable bound across both two-stage completed buffers.

## Phase 2: Make DAG Failure Cancellation Bounded

### Task 4: Add failing dependency-graph cancellation tests

**Files:**

- Modify: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/ConcurrencyPoolContractTests.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/RuleGraphCompilerTests.cs`

**Step 1: Test the generic pool.**

Create two ready nodes at DOP 2. One throws a sentinel `InvalidOperationException`; the other waits on the supplied cancellation token and exposes that cancellation was observed. Assert the pool rethrows the sentinel only after the sibling observed cancellation, and that no dependent node starts.

**Step 2: Test the rule adapter.**

Build two independent `RuleGraphExecutionNode` instances, with one failing and one cancellation-aware. Assert no telemetry/result is published for an unstarted dependent and that graph order remains unchanged for successful runs.

**Step 3: Run the new tests.**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore --filter FullyQualifiedName~RunDependencyGraphAsync_WhenReadyNodeFails
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore --filter FullyQualifiedName~RuleGraphCompilerTests
```

Expected: the generic-pool test fails before implementation because sibling work receives only the caller token.

### Task 5: Link DAG cancellation to a single run

**Files:**

- Modify: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Modify: `src/NL.Concurrency/DependencyExecutionResult.cs` only if cancellation-drain telemetry is added

**Step 1: Create a linked `CancellationTokenSource` at `RunDependencyGraphAsync` entry.**

Pass its token into every `DependencyWorkItem.ExecuteAsync` invocation. Preserve the caller token's cancellation behavior.

**Step 2: Capture first failure, cancel siblings, and drain them.**

On the first failed completion, store `ExceptionDispatchInfo`, cancel the linked source, await all already-running tasks while swallowing only their follow-on failures, then rethrow the captured first failure. Do not schedule another ready node after the failure is captured.

**Step 3: Verify.**

Run the commands in Task 4 plus the full `ConcurrencyPoolContractTests` class.

**Step 4: Commit.**

```powershell
git add src/NL.Concurrency tests/RoslynDeletionPrototype.PerformanceTests/Concurrency tests/RoslynDeletionPrototype.HostTests/Application/RuleGraphCompilerTests.cs
git commit -m "Cancel running DAG siblings after the first failure"
```

**Acceptance gate:** Failure latency is bounded by cancellation-aware sibling cleanup, not by unrelated successful work duration.

## Phase 3: Remove Avoidable Scheduling And Result Retention

### Task 6: Introduce a one-submission CPU-bound ordered path

**Files:**

- Modify: `src/NL.Concurrency/IConcurrencyPool.cs`
- Modify: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Modify: `src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/ConcurrencyPoolContractTests.cs`
- Modify: `tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**Step 1: Add a failing contract test.**

Add `SelectCpuBoundOrdered_WhenWorkCompletesOutOfOrder_ReturnsInputOrder`. Use synchronous delegates, DOP 3, and a concurrency counter. Assert source-ordered results and a peak no larger than DOP. Keep the existing pre-first-await blocking test unchanged; it protects the async API's compatibility behavior.

**Step 2: Add `SelectCpuBoundOrdered`.**

The API takes `Func<TSource, int, CancellationToken, TResult>`. It owns exactly `min(sourceCount, DOP)` worker tasks, invokes CPU work directly inside those workers, stores results by source index, and cancels outstanding work on first failure. It must not create an inner `Task.Run` per source item.

**Step 3: Migrate only confirmed synchronous CPU work.**

Change `PartitionedSyntaxPass` from `SelectOrderedAsync(... Task.FromResult(...))` to the CPU-bound method. Do not migrate directory analysis, CPG admission, shard I/O, or other delegates that await.

**Step 4: Verify semantic equivalence.**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore --filter FullyQualifiedName~ConcurrencyPoolContractTests
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore --filter "FullyQualifiedName~NLCPGPartitionedBuilderTests|FullyQualifiedName~CpgExecutionMatrixTests"
```

Expected: all graph snapshots remain equivalent to DOP 1.

### Task 7: Make `ForEachAsync` a no-result worker loop

**Files:**

- Modify: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Modify: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/ConcurrencyPoolContractTests.cs`

**Step 1: Add a behavior test.**

Run a large synthetic source list with a bounded concurrency counter and cancellation-aware delegates. Assert every source is observed exactly once, concurrency stays within DOP, and cancellation prevents newly queued items from starting.

**Step 2: Implement the worker loop.**

Use the same next-index and first-failure rules as ordered selection, but retain no `TResult[]` and no per-item completion result after the delegate finishes. Preserve the public `Task` return type.

**Step 3: Add an allocation measurement only as diagnostics.**

In a non-gating performance test or benchmark harness, compare the old result-array path and the new foreach path for a fixed source count after warmup. Record allocations, not a brittle absolute threshold.

**Step 4: Verify and commit.**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore --filter FullyQualifiedName~ConcurrencyPoolContractTests
git add src/NL.Concurrency tests/RoslynDeletionPrototype.PerformanceTests/Concurrency
git commit -m "Avoid result retention for bounded foreach work"
```

**Acceptance gate:** CPU selection uses one submission per active worker; foreach execution retains no source-sized result array; existing async compatibility remains intact.

## Phase 4: Observe Before Changing Capacity

### Task 8: Add operation-level pool telemetry without changing pool ownership

**Files:**

- Create: `src/NL.Concurrency/ConcurrencyOperationTelemetry.cs`
- Modify: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Modify: `src/NLISSN.Core/Pipeline/ExecutionRuntime.cs`
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
- Modify: `src/NLISSN.Core/Pipeline/RuleGraphExecutor.cs`
- Modify: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/ConcurrencyPoolContractTests.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Add a focused observer contract.**

Define a BCL-only `IConcurrencyPoolTelemetrySink` or optional constructor callback owned by `NL.Concurrency`. Each completed operation reports operation kind, source count, requested DOP, peak active work, peak completed-buffer items, peak retained records, elapsed time, cancellation status, and failure-drain elapsed time. Do not make a telemetry failure affect scheduling.

**Step 2: Wire one runtime-owned sink.**

`AnalysisRuntime` constructs or receives the pool with the sink. Directory and rule-graph layers append their existing domain metrics, such as CPG admission wait and publication wait, instead of reimplementing pool counters.

**Step 3: Write behavior tests.**

Assert a successful ordered operation emits one complete record with a bounded peak. Assert a DAG failure emits cancellation and non-negative drain time. Use a recording sink; do not assert wall-clock absolute values.

**Step 4: Verify.**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore --filter FullyQualifiedName~ConcurrencyPoolContractTests
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore --filter FullyQualifiedName~PipelineComponentTests
```

### Task 9: Run a measured DOP matrix and make no default change without evidence

**Files:**

- Modify: `docs/plans/2026-07-29-concurrency-pool-safety-backpressure-execution-proposal.md` with the recorded result table only after all semantic gates pass
- Modify: `progress.md` only when this becomes the active implementation feature
- Modify: `feature_list.json` only when a feature ID and definition of done are approved

**Step 1: Choose one fixed real-source sample and record its revision.**

**Step 2: Run warmed DOP 1, 8, 12, and 16 samples sequentially.**

Capture wall time, allocation, heap, working set, ThreadPool counters, CPG admission wait, ordered-buffer peak, DAG ready/concurrent peaks, and graph/rule snapshots.

**Step 3: Compare semantic snapshots before comparing speed.**

Reject a faster configuration when graph nodes/edges, rule decisions, diagnostics, rewritten source, or persisted replay differ from DOP 1.

**Step 4: Select an action.**

Retain defaults unless repeated warmed samples demonstrate a material win without increased p95 queueing or memory. Any DOP-default proposal is a follow-up feature, not part of this plan.

**Acceptance gate:** Capacity decisions have attributable telemetry and semantic snapshots; a single timing sample never changes a default.

## Final Verification And Rollback

Run production builds and tests sequentially:

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\NL.Concurrency\NL.Concurrency.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-build --filter FullyQualifiedName~Concurrency
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build --filter "FullyQualifiedName~NLCPGPartitionedBuilderTests|FullyQualifiedName~CpgExecutionMatrixTests"
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build --filter "FullyQualifiedName~RuleGraphCompilerTests|FullyQualifiedName~PipelineComponentTests"
pwsh -File .\scripts\check-harness-consistency.ps1 -SkipCliSmoke
git diff --check
```

The harness script is expected to remain independently blocked by stale deleted-project paths until its owning migration task repairs it. Report that wiring failure separately from compilation and semantic-test results.

Keep each task in a separate commit. Revert the task commit if a source-order, DOP-equivalence, cancellation, or graph snapshot gate fails. Do not bundle a rollback with unrelated scheduler, persistence, or default-DOP changes.

## Execution Evidence (2026-07-29)

| Gate | Result |
| --- | --- |
| Pool contracts | `ConcurrencyPoolContractTests`: 26/26 passed. |
| Ordered-window and CPG DOP equivalence | Focused `OrderedPartitionWindow`, `TwoStagePartitionWindow`, and `CpgExecutionMatrixTests`: 42/42 passed. |
| Rule and runtime integration | `RuleGraphCompilerTests`: 17/17; `PipelineComponentTests`: 114/114. |
| Production builds | `NL.Concurrency`, `NLCPG`, `NLISSN.Core`, and `NLISSN` built with 0 warnings and 0 errors. |

The stale `scripts/Run-PerformanceSuite.ps1` referenced the deleted
`RoslynPrototype` project. This execution adds
`scripts/Run-ConcurrencyPoolPerformance.ps1`, which drives the current NLISSN CLI,
requires a successful terminal runtime-log event, and rejects a run when its graph or
rule snapshot differs from the DOP 1 baseline. The original worktree's harness check
still separately fails on stale `src/RoslynPrototype/Program.cs`.

The admission controller is runtime-local and only waits at asynchronous DAG
boundaries. Existing synchronous ordered CPG APIs retain their local completed-item
and record bounds; changing them to wait for a shared asynchronous reservation would
starve ThreadPool rule workers. A future async CPG API propagation is required before
using the global byte budget to gate those synchronous CPG stages.

### Real-Source Matrix

The matrix ran sequentially on 2026-07-29 using the current dirty
`codex/concurrency-pool-safety` worktree at base commit
`66304494dca41b25cdcb8663741ed09f5b84cd32`. The source was
`D:\lodes\TR\Backup\New1.27\1.45\TR\Terraria\Projectile.cs`, SHA-256
`b2ccceb7fd4a60a8cef72826330aa6350ffa0da47fcda18f93ed44a72d831708`.
Each DOP had one warmup and three measurements, with
`--skip-rewrite --no-diff --target-name Projectile`. The evidence root is
`Build\concurrency-projectile-matrix-20260729-valid\summary.json`.

Every warmup and measurement emitted the identical snapshot:
`nodes=1586944|edges=3716000|seed=1|propagated=1|lifted=3|decisions=1|edits=0|diagnostics=0`.

| DOP | Median elapsed ms | Median working set bytes | Median allocated bytes | Median ThreadPool threads | Queue p95 ms | Aging promotions |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 69,718 | 5,506,465,792 | 10,890,647,024 | 4 | 0 | 0 |
| 8 | 69,059 | 5,342,826,496 | 10,928,349,344 | 16 | 0 | 0 |
| 12 | 66,172 | 5,528,252,416 | 10,947,765,832 | 22 | 0 | 0 |
| 16 | 69,414 | 5,357,228,032 | 10,956,553,240 | 17 | 0 | 0 |

DOP 12 has the lowest observed median, 5.1% below DOP 1, but neither allocated bytes
nor working-set peaks improve consistently. This is one fixed single-file workload,
not authority to change the default. The CLI defaults remain unchanged. The runtime log
now exposes process samples, per-operation queue waits, ordered-buffer peaks, DAG ready
peaks, admission turns, and terminal graph/rule snapshots for later directory-level
measurements.
