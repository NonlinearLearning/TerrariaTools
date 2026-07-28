# Concurrency Pool Extraction Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Extract task scheduling from `src` projects into a BCL-only `src/NL.Concurrency` project with one injectable, bounded-concurrency API while preserving current output, ordering, cancellation, memory, and admission contracts.

**Architecture:** `NL.Concurrency` owns only task scheduling over the CLR `System.Threading.ThreadPool`; it does not own threads globally and does not replace the CLR pool. It exposes ordered selection, ordered commit, two-stage ordered commit, bounded foreach, and dependency-graph scheduling. `NLISSN.Core` keeps deletion-rule graph types and execution telemetry, while `NLCPG` keeps graph materialization, CPG admission, persistence queues, and resource ownership.

**Tech Stack:** .NET 10, `Task`, `Parallel` replacement through bounded worker loops, `CancellationToken`, xUnit, existing CPG contract tests and deletion-rule performance tests.

---

## Scope And Non-Goals

### Extract into the new project

- The worker-loop in `src/NLISSN.Core/Pipeline/ExecutionRuntime.cs` currently named `BoundedRuleStageScheduler`.
- The three scheduling shapes in `src/NLCPG/Builder/BoundedPartitionWorkWindow.cs`: ordered selection, ordered commit, and two-stage ordered commit.
- The ready/running task scheduling in `src/NLISSN.Core/Pipeline/RuleGraphExecutor.cs`.
- Direct scheduling calls in these files:
  - `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
  - `src/NLISSN.Core/Decision/DecisionModel.cs`
  - `src/NLCPG/Builder/CpgShardBuildCoordinator.cs`
  - `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`

### Keep in their owning modules

- `Channel` producer/consumer pipelines in `CpgShardBuildSession`, `CpgCatalogBatchWriter`, and `TextLogFileSink`.
- `CpgShardStoreLock` named semaphores and registered waits; these provide cross-process exclusion.
- `CpgBuildAdmissionBudget`, `SemaphoreSlim` reorder slots, `lock`, `ConcurrentDictionary`, `Interlocked`, and `Volatile`; these protect local state or resource lifetime rather than schedule independent work.
- Public CLI options and their current meanings: `--max-degree-of-parallelism`, `--cpg-max-degree-of-parallelism`, and the directory/group/helper switches.

### Invariants

1. The new project has no project references and only BCL dependencies.
2. The CLR `ThreadPool` remains the real execution resource. The new API owns bounded work submission and completion handling only.
3. `NLCPG` workers read Roslyn facts only. Graph nodes, edges, de-duplication, and final order remain materialized by the stable calling thread.
4. Ordered APIs return or commit by source index even when work completes out of order.
5. Ordered-commit APIs retain the current worker limit, reorder allowance, record-count limit, cancellation behavior, and first-observed exception behavior.
6. Rule-DAG execution starts only nodes whose dependencies completed, selects ready nodes by compiled graph order, and retains peak-ready/peak-concurrent metrics.
7. CPG admission stays outside the pool. A caller must obtain a `CpgBuildAdmissionBudget` lease before submitting CPG work.
8. No direct production use remains of `Task.Run`, `Task.WhenAll`, `Task.WhenAny`, `Parallel.ForEach`, or `Parallel.ForEachAsync` for independent task scheduling outside `NL.Concurrency`; the explicit Channel and cross-process-lock exclusions above remain allowed.

## API Contract

Create `NL.Concurrency/IConcurrencyPool.cs` and use a single injected implementation, `BoundedConcurrencyPool`. The public surface should be limited to these operations:

```csharp
public interface IConcurrencyPool
{
    Task<IReadOnlyList<TResult>> SelectOrderedAsync<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task<TResult>> workItem,
        CancellationToken cancellationToken = default);

    void CommitOrdered<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        ConcurrencyWindowOptions options,
        Func<TSource, int, TResult> workItem,
        Action<TResult, int> commit,
        CancellationToken cancellationToken = default);

    void CommitTwoStageOrdered<TSource, TCollected, TPrepared, TResult>(
        IReadOnlyList<TSource> sources,
        ConcurrencyWindowOptions options,
        Func<TSource, int, TCollected> collect,
        Func<TCollected, int, TPrepared> prepare,
        Func<TPrepared, int, TResult> solve,
        Action<TResult, int> commit,
        CancellationToken cancellationToken = default);

    Task ForEachAsync<TSource>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task> workItem,
        CancellationToken cancellationToken = default);

    Task<DependencyExecutionResult<TNode, TResult>> RunDependencyGraphAsync<TNode, TResult>(
        IReadOnlyList<DependencyWorkItem<TNode, TResult>> workItems,
        int maxDegreeOfParallelism,
        IComparer<TNode> readyOrder,
        CancellationToken cancellationToken = default)
        where TNode : notnull;
}
```

`ConcurrencyWindowOptions` contains the existing `MaxDegreeOfParallelism`, `ReorderAllowance`, and `MaxCompletedRecordCount`. `DependencyWorkItem` contains a node key, immutable dependencies, and an asynchronous delegate receiving completed dependency results. Keep `RuleNodeResult`, `RuleGraphNodeTelemetry`, and all Roslyn/CPG result types out of `NL.Concurrency`.

Do not add a static global instance, a dedicated thread implementation, a custom `TaskScheduler`, Dataflow, Channels, or a new package. Create one pool per runtime/builder dependency boundary and inject it where test seams need deterministic behavior.

## Phase 0: Establish Baseline And Feature State

### Task 1: Record the new feature and current validation blocker

**Files:**

- Modify: `feature_list.json`
- Modify: `progress.md`
- Test: `scripts/check-harness-consistency.ps1`

**Step 1: Add a pending feature record**

Add `concurrency-pool-extraction` with these definition-of-done items:

- `src/NL.Concurrency` exposes and tests the five API contracts.
- All in-scope production schedulers use the new project.
- CPG ordering, rule-DAG ordering, cancellation, and existing CLI-equivalence tests pass.
- The final source inventory has no unapproved direct task scheduling call sites.

**Step 2: Update the concise handoff**

Set `progress.md` to the new feature ID. State that `init.ps1` currently fails before the health check because it still searches for deleted `src/MinimalRoslynCpg/MinimalRoslynCpg.csproj`; direct current-project builds are the temporary verification route. Do not repair `init.ps1` in this feature unless its owner explicitly expands scope.

**Step 3: Verify the expected harness result**

Run:

```powershell
pwsh -File .\init.ps1
```

Expected: exit code `1` with `[init] Missing project file. Searched: src\MinimalRoslynCpg\MinimalRoslynCpg.csproj`.

**Step 4: Verify source-facing harness consistency separately**

Run:

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1 -SkipCliSmoke
```

Expected: the same stale-path failure. Record it as external harness drift, not as a concurrency implementation failure.

**Step 5: Commit the state-only change**

```powershell
git add feature_list.json progress.md
git commit -m "Track concurrency scheduler extraction"
```

Use the repository Lore trailers. State the stale harness path in `Not-tested`.

**Acceptance gate:** The feature and blocker are explicit before code changes. No implementation has been attributed to an unrelated bootstrap failure.

## Phase 1: Add and Prove the Standalone API

### Task 2: Create the BCL-only project and test access

**Files:**

- Create: `src/NL.Concurrency/NL.Concurrency.csproj`
- Create: `src/NL.Concurrency/IConcurrencyPool.cs`
- Create: `src/NL.Concurrency/ConcurrencyWindowOptions.cs`
- Create: `src/NL.Concurrency/DependencyWorkItem.cs`
- Create: `src/NL.Concurrency/DependencyExecutionResult.cs`
- Modify: `src/NLISSN.Core/NLISSN.Core.csproj`
- Modify: `src/NLCPG/NLCPG.csproj`
- Modify: `src/NLISSN.Rules/NLISSN.Rules.csproj`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/RoslynDeletionPrototype.UnitTests.csproj`
- Create: `tests/RoslynDeletionPrototype.UnitTests/Concurrency/ConcurrencyPoolContractTests.cs`

**Step 1: Write the failing public-contract test**

Write a test that imports `NL.Concurrency`, constructs `BoundedConcurrencyPool`, and calls `SelectOrderedAsync` with three items whose completion order is `2, 1, 0`. Assert the returned sequence is `0, 1, 2`.

**Step 2: Run the test and confirm compilation fails for the intended reason**

Run:

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --filter FullyQualifiedName~ConcurrencyPoolContractTests --no-restore
```

Expected: failure because `NL.Concurrency` and `BoundedConcurrencyPool` do not exist.

**Step 3: Add only project and contract types**

Create a `net10.0`, nullable-enabled project with no `PackageReference` or `ProjectReference`. Add immutable records and the interface; do not write the implementation yet.

**Step 4: Add direct project references**

Reference `NL.Concurrency` directly from each production project that invokes it. Add the direct test reference. Do not rely on transitive project references for a shared infrastructure API.

**Step 5: Re-run the focused test**

Expected: it still fails because the concrete implementation is missing, which proves the test reaches the intended API.

**Step 6: Commit**

```powershell
git add src/NL.Concurrency src/NLISSN.Core/NLISSN.Core.csproj src/NLCPG/NLCPG.csproj src/NLISSN.Rules/NLISSN.Rules.csproj tests/RoslynDeletionPrototype.UnitTests
git commit -m "Define shared concurrency scheduling contracts"
```

**Acceptance gate:** The new project is dependency-free and tests compile far enough to fail only because the implementation is absent.

### Task 3: Implement ordered selection and bounded foreach

**Files:**

- Create: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/Concurrency/ConcurrencyPoolContractTests.cs`

**Step 1: Add failing behavior tests**

Add one test per behavior:

1. `SelectOrderedAsync` never exceeds a tracked maximum of two active work items when DOP is two.
2. `SelectOrderedAsync` observes cancellation before a queued item starts.
3. `SelectOrderedAsync` rethrows a work-item exception and awaits already-started workers before returning.
4. `ForEachAsync` accepts an asynchronous callback, limits activity, and forwards the supplied cancellation token.

Use `TaskCompletionSource` gates and `Interlocked` counters. Do not use sleeps as synchronization.

**Step 2: Confirm the tests fail**

Run the focused command from Task 2. Expected: assertion failures because the API has no behavior yet.

**Step 3: Implement the minimal shared worker loop**

Use an index claimed through `Interlocked.Increment`, a worker count clamped to source count and requested DOP, and `Task.WhenAll`. Store results by input index. Preserve cancellation checks before invoking each callback.

**Step 4: Verify focused tests**

Expected: all `ConcurrencyPoolContractTests` pass without timeout or unobserved-task output.

**Step 5: Commit**

```powershell
git add src/NL.Concurrency tests/RoslynDeletionPrototype.UnitTests/Concurrency
git commit -m "Bound ordered work on the runtime thread pool"
```

**Acceptance gate:** The API uses CLR task scheduling with bounded work submission; it does not create threads or introduce a global queue.

## Phase 2: Migrate Deletion-Rule Scheduling

### Task 4: Replace the rule-stage scheduler

**Files:**

- Modify: `src/NLISSN.Core/Pipeline/ExecutionRuntime.cs`
- Modify: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/BoundedRuleStageSchedulerConcurrencyTests.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Adapt existing scheduler tests before deleting the old type**

Rename the test fixture for the new API. Port existing order, cancellation, peak-concurrency, and real-source scheduler tests to `BoundedConcurrencyPool` through `AnalysisRuntime.ConcurrencyPool`.

**Step 2: Run the old-focused tests**

Expected: compile failures after the test expects `AnalysisRuntime.ConcurrencyPool`, proving the runtime seam is not yet wired.

**Step 3: Replace the seam**

Remove `IRuleStageScheduler` and `BoundedRuleStageScheduler`. Add an `IConcurrencyPool` constructor argument to `AnalysisRuntime`, default it to `BoundedConcurrencyPool`, and preserve the exact pool instance through `InvalidateCaches()` and `NextEpoch()`.

**Step 4: Convert stage callers**

Change `Runtime.Scheduler.RunOrderedAsync(...)` calls to `Runtime.ConcurrencyPool.SelectOrderedAsync(...)`. Preserve all current degree and cancellation arguments. Do not alter `EnableGroupParallelism` decision logic.

**Step 5: Verify**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --filter FullyQualifiedName~Concurrency --no-restore
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --filter FullyQualifiedName~PipelineComponentTests --no-restore
```

**Step 6: Commit**

```powershell
git add src/NLISSN.Core tests/RoslynDeletionPrototype.PerformanceTests tests/RoslynDeletionPrototype.HostTests
git commit -m "Route deletion stages through shared concurrency pool"
```

**Acceptance gate:** Same-group execution remains serial, different eligible groups retain the configured bounded parallelism, and runtime epoch transitions keep the injected pool.

### Task 5: Move generic DAG scheduling below the rule layer

**Files:**

- Modify: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Modify: `src/NL.Concurrency/DependencyWorkItem.cs`
- Modify: `src/NLISSN.Core/Pipeline/RuleGraphExecutor.cs`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/Concurrency/ConcurrencyPoolContractTests.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/RuleGraphCompilerTests.cs`

**Step 1: Add a failing generic graph test**

Create nodes `A`, `B`, and `C` where `C` depends on `A` and `B`. Block `A` and `B`, assert `C` has not started, release both, then assert `C` receives both results. Add a tie-order test for independent nodes and a max-DOP test.

**Step 2: Implement `RunDependencyGraphAsync`**

Maintain remaining-dependency counts, a `SortedSet<TNode>` using the caller comparator, a result dictionary, and the running task set inside `NL.Concurrency`. On completion, release downstream nodes. Await the completed task before publishing its result. Return only generic node/result and concurrency metrics.

**Step 3: Rework `RuleGraphExecutor` as an adapter**

Keep `RuleNodeInputs`, `RuleNodeResult`, rule telemetry, and `GraphOrderComparer` in `NLISSN.Core`. Construct generic work items whose delegates build rule inputs and record per-node elapsed time. Feed them to `AnalysisRuntime.ConcurrencyPool`; do not keep raw `Task.Run`, `Task.WhenAny`, or scheduling collections in `RuleGraphExecutor`.

**Step 4: Verify behavior and telemetry**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --filter FullyQualifiedName~RuleGraphCompilerTests --no-restore
```

Expected: graph result order, input/output counts, peak-ready count, and peak-concurrent count match existing assertions.

**Step 5: Commit**

```powershell
git add src/NL.Concurrency src/NLISSN.Core/Pipeline/RuleGraphExecutor.cs tests/RoslynDeletionPrototype.UnitTests/Concurrency tests/RoslynDeletionPrototype.HostTests/Application/RuleGraphCompilerTests.cs
git commit -m "Centralize dependency graph task scheduling"
```

**Acceptance gate:** Rule semantics remain in `NLISSN.Core`; the generic project knows no rule types or Roslyn types.

## Phase 3: Migrate CPG Ordered Windows

### Task 6: Replace `BoundedPartitionWorkWindow` with ordered pool APIs

**Files:**

- Modify: `src/NL.Concurrency/BoundedConcurrencyPool.cs`
- Delete: `src/NLCPG/Builder/BoundedPartitionWorkWindow.cs`
- Modify: `src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/NLCPG/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs`
- Modify: `tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/Concurrency/ConcurrencyPoolContractTests.cs`

**Step 1: Replace reflection tests with behavior tests**

The current contract test asserts the private helper type and methods. Replace these assertions with public builder tests that deliberately complete later partitions first and assert stable snapshot, edge order, source-order publication, and the configured completed-record bound.

**Step 2: Add direct pool tests for both ordered-commit APIs**

For `CommitOrdered`, make index zero block while later indexes complete. Assert no `commit` occurs out of order and no more results than `ReorderAllowance` remain uncommitted. For `CommitTwoStageOrdered`, assert `prepare` follows source order while `collect` and `solve` may finish out of order.

**Step 3: Implement ordered commit exactly once**

Move `CompletedWorkItem`, worker waiting, first-failure cleanup, retained-record accounting, and cancellation checks into `BoundedConcurrencyPool`. Preserve the existing synchronous API so current synchronous CPG builder entrypoints do not become asynchronous in this extraction.

**Step 4: Inject or construct the pool at the CPG boundary**

Add an optional `IConcurrencyPool` dependency to `NLCPGBuilder` or `NLCPGBuilderOptions` using the repository's existing dependency style. Do not create an application-layer dependency. Every partition pass uses that same instance.

**Step 5: Delete the old helper and migrate all three callers**

Use `SelectOrderedAsync` in `PartitionedSyntaxPass`, `CommitOrdered` in `PartitionedOperationPass`, and `CommitTwoStageOrdered` in `DataFlowPass`. Retain all existing `NLCPGBuilderOptions` calculations for DOP, reorder allowance, and maximum completed records.

**Step 6: Verify CPG contracts**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --filter "FullyQualifiedName~NLCPGPartitionedBuilderTests|FullyQualifiedName~CpgShardBuildCoordinatorTests" --no-restore
```

Expected: DOP graph equivalence and streaming/source-order assertions pass; no test refers to `BoundedPartitionWorkWindow`.

**Step 7: Commit**

```powershell
git add src/NL.Concurrency src/NLCPG tests/RoslynDeletionPrototype.ContractTests/Cpg tests/RoslynDeletionPrototype.UnitTests/Concurrency
git commit -m "Reuse shared ordered windows for CPG construction"
```

**Acceptance gate:** CPG retains caller-thread materialization and bounded out-of-order retention. No source or graph ordering contract is relaxed.

## Phase 4: Migrate Remaining Independent Scheduling

### Task 7: Replace bounded foreach call sites

**Files:**

- Modify: `src/NLCPG/Builder/CpgShardBuildCoordinator.cs`
- Modify: `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`
- Modify: `tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/Concurrency/ConcurrencyPoolContractTests.cs`

**Step 1: Add CPG coordinator concurrency test**

Use its existing checkpoint observer to assert shard exports never exceed `MaxConcurrentShardExports`, cancellation stops additional exports, and the first export failure is observed by the caller.

**Step 2: Replace `Parallel.ForEachAsync`**

Call `IConcurrencyPool.ForEachAsync` with the existing `MaxConcurrentShardExports` and cancellation token. Preserve active-export metrics exactly.

**Step 3: Add parameter-shrink equivalence coverage**

Choose an existing fixture that exercises helper parallelism. Run once with helper parallelism disabled and once enabled; assert normalized rewrite proposals match.

**Step 4: Replace `Parallel.ForEach`**

Use a synchronous wrapper only if the surrounding algorithm must remain synchronous. Pass the existing helper DOP and preserve all `ConcurrentBag`, `ConcurrentDictionary`, `Interlocked`, and `Volatile` ownership in `ParameterShrinkAnalyzer`.

**Step 5: Verify**

Run the focused CPG contract test and the relevant parameter-shrink host/unit test selected in Step 3.

**Step 6: Commit**

```powershell
git add src/NLCPG/Builder/CpgShardBuildCoordinator.cs src/NLISSN.Rules/Propose/ParameterShrink tests/RoslynDeletionPrototype.ContractTests/Cpg tests/RoslynDeletionPrototype.UnitTests/Concurrency
git commit -m "Use shared bounded foreach scheduling"
```

**Acceptance gate:** Each call site keeps its own domain data structures and metrics; the pool only controls work dispatch.

### Task 8: Remove nested task submission in NLISSN

**Files:**

- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/Application/DirectoryAnalysisUseCaseTests.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Add a directory regression**

Use at least three in-memory files whose analysis completes out of order. Assert output file ordering, rewrite plans, and publication telemetry remain source ordered at DOP one and greater than one.

**Step 2: Remove the directory's nested `Task.Run`**

The enclosing `SelectOrderedAsync` already schedules work. Execute the synchronous analysis delegate directly in that callback after CPG admission is acquired. Preserve `PushCpgBuildAdmissionLease`, cancellation, and publication-lock behavior.

**Step 3: Replace decision-stage direct task submission**

Route the decision work through `AnalysisRuntime.ConcurrencyPool`; preserve the current index/result order and cancellation behavior.

**Step 4: Verify focused deletion-rule tests**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --filter FullyQualifiedName~DirectoryAnalysisUseCaseTests --no-restore
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --filter FullyQualifiedName~PipelineComponentTests --no-restore
```

**Step 5: Commit**

```powershell
git add src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs src/NLISSN.Core/Decision/DecisionModel.cs tests/RoslynDeletionPrototype.UnitTests/Application tests/RoslynDeletionPrototype.HostTests/Application
git commit -m "Eliminate nested deletion analysis task scheduling"
```

**Acceptance gate:** Directory publication and decision output remain deterministic; no CPG lease is held outside the submitted analysis duration.

## Phase 5: Audit, Documentation, And Final Verification

### Task 9: Prove the extraction boundary

**Files:**

- Modify: `tests/ArchitectureTests/Program.cs` only when an existing project/dependency guard needs the new project listed.
- Modify: `progress.md`
- Modify: `feature_list.json`

**Step 1: Run the final inventory**

Run:

```powershell
rg -n -g '*.cs' "Task\.Run\(|Task\.WhenAll\(|Task\.WhenAny\(|Parallel\.ForEach\w*\(|new ParallelOptions" src
```

Expected: independent-work scheduling hits are confined to `src/NL.Concurrency`. Review every remaining hit against the approved exclusions: Channel consumers, their drain-task startup, cross-process lock waiting, and test-only helpers. Update this plan's scope if a new production scheduler is discovered.

**Step 2: Build projects sequentially**

Run these commands one at a time because projects share output trees:

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\NL.Concurrency\NL.Concurrency.csproj
dotnet build .\src\NLCPG\NLCPG.csproj
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj
dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj
dotnet build .\src\NLISSN.Application\NLISSN.Application.csproj
dotnet build .\src\NLISSN\NLISSN.csproj
```

Expected: zero errors. Report restore/environment failures separately from compilation failures.

**Step 3: Run test tiers sequentially**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-build
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-build --filter FullyQualifiedName~Concurrency
```

Expected: all selected tests pass. Do not call a resource-exhausted, cancelled, or incomplete run a performance result.

**Step 4: Run documentation and diff checks**

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1 -SkipCliSmoke
git diff --check
```

Expected: `git diff --check` passes. The harness script will remain blocked until its stale project paths are repaired in its own task; record that status in `progress.md`.

**Step 5: Finalize state and commit**

Mark `concurrency-pool-extraction` complete only after every semantic test and source inventory gate passes. Keep the harness-path issue as a separate current blocker when still unresolved.

```powershell
git add feature_list.json progress.md tests/ArchitectureTests/Program.cs
git commit -m "Verify centralized concurrency scheduling boundary"
```

**Acceptance gate:** The implementation uses one shared scheduling API, preserves all documented semantic boundaries, and has evidence for compilation, tests, inventory, and remaining harness drift.

## Rollback Strategy

Every migration task is independently reversible because its API boundary is narrow:

- Restore `BoundedRuleStageScheduler`, `RuleGraphExecutor` task handling, or `BoundedPartitionWorkWindow` from the immediately preceding commit if a focused contract fails.
- Keep the standalone API test project even when a caller migration reverts; it remains a validated dependency-free library.
- Do not alter default DOP values, CPG admission policy, Channel capacities, persistence behavior, or resource locks in this feature. Those changes require separate evidence and a separate proposal.

## Execution Record (2026-07-28)

- Implemented `src/NL.Concurrency` with ordered selection, ordered commit, two-stage ordered commit, bounded foreach, and dependency graph scheduling; all production callers in this plan now use it.
- A start-gate regression was found during CPG shard export testing: invoking caller delegates while holding the gate serialized async delegates that block before their first `await`. The pool now registers the work under the gate and invokes it in the registered task. `SelectOrderedAsync_WhenWorkItemBlocksBeforeItsFirstAwait_StartsTheRestOfTheWorkerWindow` covers this behavior.
- Sequential production builds passed with zero warnings and zero errors for `NL.Concurrency`, `NLCPG`, `NLISSN.Core`, `NLISSN.Rules`, `NLISSN.Application`, and `NLISSN`.
- Passed: concurrency suite 17/17, ContractTests excluding two pre-existing architecture/path guards 218/218, HostTests 449/449, and `LayoutArchitectureTests` 3/3.
- Independent blockers: UnitTests cannot compile because `StructureViewBuilderTests.cs` imports missing `NLISSN.Core.Pipeline`; the complete ContractTests run retains two pre-existing failures in `TestProjectBoundaryTests` and `ArchitectureBoundaryTests`; `init.ps1` and `check-harness-consistency.ps1` still reference deleted migration paths.
