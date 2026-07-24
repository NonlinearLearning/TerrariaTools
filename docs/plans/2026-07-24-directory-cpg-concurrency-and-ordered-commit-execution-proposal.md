# Directory And CPG Concurrency Execution Proposal

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Eliminate the medium-fixture long-tail risk caused by nested directory and CPG work while preserving deterministic graph, rule, and rewrite output.

**Architecture:** First expose the existing CPG freeze and ordered-window evidence without changing scheduling. Then admit CPG builds through a shared runtime budget, followed by an optional fair partition-level budget only when admission telemetry proves it is needed. Keep graph materialization and final directory aggregation deterministic; increase analysis look-ahead only through bounded, measured reorder windows.

**Tech Stack:** .NET 10, Roslyn, xUnit, existing text runtime/analysis logs, PowerShell fixture runners.

---

## Problem And Evidence

The 2026-07-24 DOP split measurements used the binary built from the `codex/cpg-dop-split` worktree. Small (103 files) and mixed stress (31 files) improved with `(directory=12, cpg=12)`, while medium (103 files) produced `(12,12)` samples of `57.600 / 31.026 / 27.493 s` and a `93.845 s` tail for `(1,12)`. Isolated `WorldGen.cs`, `NPC.cs`, and `Projectile.cs` builds were graph-equal at CPG DOP 1 and 12 and improved by 8.2%, 8.7%, and 6.1% respectively.

The evidence supports nested-concurrency and ordering hypotheses. It does not attribute the tail to a particular pass, GC, logging, or freeze-index operation. This proposal keeps the default DOP unchanged until attribution and semantic gates pass.

## Scope

In scope:

- Runtime telemetry for CPG budget wait, ordered result buffering, source-order publication wait, and freeze substeps.
- A shared CPG budget owned by `DeletionAnalysisRuntime` and passed to each builder invocation.
- A bounded CPG ordered-analysis window that retains serial deterministic materialization.
- Directory result-backlog measurement and an optional bounded publication window.
- Focused contract/host/performance regression coverage and controlled real-source runs.

Out of scope:

- Changing the default directory or CPG DOP.
- Relaxing graph, Mark, Decision, Rewrite, diff, or source-order contracts.
- Replacing `RoslynCpgGraphIndex` sorting or removing indexes before telemetry establishes their share of the tail.
- Persisted-shard throughput changes, SQLite tuning, or a full 1,503-file matrix before the medium gate passes.

## Invariants

1. CPG workers only read Roslyn semantic facts. Graph nodes, edges, de-duplication, and ordering remain materialized on a stable caller thread.
2. DOP 1, 8, 12, 14, and 16 retain complete frozen-graph equality where existing tests cover them.
3. Directory results, normalized Mark/Decision output, rewrite plans, rewritten source, and diff stay deterministic.
4. A cancelled or failed build releases every acquired budget lease and produces no completed event.
5. Benchmark samples require a successful `files=<expected>/<expected>` completion event. Timeout, resource exhaustion, missing logs, or an incomplete completion event invalidate the sample.

## Phase 0: Add Attribution Before Scheduling Changes

### Task 1: Expose CPG freeze and ordered-window telemetry

**Files:**

- Modify: `src/MinimalRoslynCpg/Builder/BoundedPartitionWorkWindow.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Modify: `src/Host/Logging/RunTextLogWriter.cs`
- Test: `tests/RoslynDeletionPrototype.ContractTests/Cpg/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Test: `tests/RoslynDeletionPrototype.HostTests/Logging/TextLogSystemTests.cs`

1. Write failing contract tests that force lower-order work to wait while later work completes. Assert the telemetry reports nonzero `CommitWaitMilliseconds`, `WindowBlockedMilliseconds`, and a bounded out-of-order count without changing the committed result order.
2. Add an immutable ordered-window telemetry record to the builder telemetry. Record active worker peak, completed-but-uncommitted peak, completed record-count peak, wait-to-commit time, and time during which a full reorder window prevented new analysis scheduling.
3. Preserve the current `RunOrdered` behavior in this task. Its first change is observability only; worker count, source order, cancellation, and exception propagation remain unchanged.
4. Add the existing `RoslynCpgFreezeTelemetry` substeps to the CPG summary: deterministic NodeId assignment, edge ordering, node ordering, snapshot hash, edge bucket population, adjacency, kind adjacency, edge-kind index, node-kind index, and file-path index. Retain aggregate `freezeMs` for compatibility.
5. Add log tests for the old aggregate fields and new diagnostic fields. The benchmark profile must include them; lower profiles remain filtered according to the existing filter contract.
6. Run the focused tests and commit only these telemetry files.

**Acceptance gate:** A controlled out-of-order fixture proves that the telemetry identifies head blocking. Existing CPG summaries retain their current fields and graph snapshots remain equal.

### Task 2: Measure the medium fixture with durable per-run evidence

**Files:**

- Modify: `scripts/Run-PerformanceSuite.ps1`
- Modify: `scripts/tests/Run-PerformanceSuite.tests.ps1`
- Create: `Build/cpg-dop-medium-YYYYMMDD/` runtime and analysis logs outside source control

1. Write a script test that rejects a run without a successful `cat=run evt=completed status=completed` event and the expected `files=103/103` field.
2. Extend the result projection with CPG summary fields, freeze substeps, ordered-window telemetry, peak heap/working/private bytes, allocation, Gen0/1/2, and ThreadPool data. Keep wall-clock distinct from summed per-file phase time.
3. Store command line, source fingerprint, git SHA, SDK, processor count, fixture manifest hash, directory DOP, requested CPG DOP, and output paths in the summary JSON.
4. Run one warmup and at least three sequential measurements for `(12,1)`, `(1,12)`, and `(12,12)` from the same built binary. Preserve logs for every warmup and measurement.
5. Classify the longest sample by phase and budget/window waits. Do not start an implementation phase when the logs are incomplete or the dominant category is ambiguous.

**Acceptance gate:** The medium result table attributes each tail to at least one measurable category: CPG budget wait, ordered-window block, freeze substep, GC/memory growth, ThreadPool queueing, rule phase, or log write. “CPG is slow” is not an acceptable attribution.

## Phase 1: Bound Nested CPG Admission

### Task 3: Add a shared CPG build-admission budget

**Files:**

- Create: `src/MinimalRoslynCpg/Builder/CpgBuildAdmissionBudget.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Modify: `src/RoslynPrototype/RuleServices/ExecutionRuntime.cs`
- Modify: `src/Application/DeletionApplicationService.cs`
- Modify: `src/Host/DeletionDirectoryAnalysisService.cs`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- Test: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/BoundedRuleStageSchedulerConcurrencyTests.cs`

1. Write a failing concurrency test with multiple directory files that block inside a test CPG build. Assert that the sum of granted CPG degrees never exceeds the configured total budget and that cancellation releases every lease.
2. Add an internal `CpgBuildAdmissionBudget` in `MinimalRoslynCpg.Builder`. It owns the total CPU budget and returns a disposable lease whose `GrantedDegree` is the only DOP supplied to one CPG builder invocation.
3. Store one shared budget in `DeletionAnalysisRuntime`, preserving the same instance through `InvalidateCaches()` and `NextEpoch()`. The budget is an internal seam for this phase; do not introduce a new CLI default.
4. Make directory scheduling await admission before entering the synchronous builder call. Waiting for admission must not occupy a worker executing CPG work. Remove the redundant nested `Task.Run` only when the focused scheduler tests prove the same cancellation and source-order behavior.
5. Pass the lease degree to `RoslynCpgBuilderOptions.MaxDegreeOfParallelism`; release the lease in `finally`, including builder failure and cancellation paths.
6. Emit `requestedCpgDop`, `grantedCpgDop`, `cpgAdmissionWaitMs`, active lease count, and granted-degree high-water mark in the CPG summary.
7. Run host option-flow, directory-equivalence, cancellation, and graph-equality tests. Commit the admission budget separately from later fairness work.

**Acceptance gate:** At every instant, granted CPG degrees are bounded by the configured total. Directory work can wait asynchronously, no lease leaks after failure/cancellation, and DOP output stays semantically equal. This phase does not alter the default budget policy.

### Task 4: Choose and validate an admission policy

**Files:**

- Modify: `src/MinimalRoslynCpg/Builder/CpgBuildAdmissionBudget.cs`
- Modify: `src/RoslynPrototype/RuleServices/ExecutionRuntime.cs`
- Test: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/BoundedRuleStageSchedulerConcurrencyTests.cs`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

1. Add deterministic policy tests for a total budget of 12 with simultaneous requests of 12, 6, 2, and 1. Assert fairness, boundedness, and no starvation of later files.
2. Evaluate two internal policies using the Phase 0 medium report: whole-build leases and a capped-per-file fair lease. Select only one policy for the runtime default; leave the other in test-only comparison code or remove it.
3. The selected policy must keep at least two eligible CPG files progressing when work exists unless the requested per-file degree exhausts the explicit total budget. Document this exception in telemetry.
4. Add a regression for a slow first file plus fast later files. Assert a later file eventually receives a lease and the source-order result aggregation stays deterministic.

**Acceptance gate:** The chosen policy is justified by recorded medium telemetry, has a deterministic test suite, and keeps CPG admission bounded without a starvation path.

## Phase 2: Reduce Ordered-Commit Head Blocking

### Task 5: Add a bounded reorder allowance to CPG partition analysis

**Files:**

- Modify: `src/MinimalRoslynCpg/Builder/BoundedPartitionWorkWindow.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Test: `tests/RoslynDeletionPrototype.ContractTests/Cpg/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. Write a failing deterministic test with a delayed first partition and multiple fast later partitions. Assert that a reorder allowance starts additional analysis, while `commit` still occurs strictly in ascending partition order.
2. Split the old condition `activeWorkers.Count + completedResults.Count < workerCount` into two limits: active analysis workers remain at CPG DOP, and completed-but-uncommitted results remain below an explicit reorder allowance.
3. Start with an allowance equal to the granted CPG degree. Add a hard record-count budget based on `OperationPartitionResult.Records.Count`; when it would be exceeded, stop look-ahead and record the reason.
4. Keep graph mutation, operation inventory updates, streaming publication, cancellation, and exception behavior inside the source-ordered commit callback. Workers continue to collect Roslyn facts only.
5. Extend telemetry with allowance, retained record count, and the identity/span of the oldest uncommitted partition. Do not serialize source text or Roslyn objects into text logs.
6. Run DOP graph-snapshot, exact DataFlow-edge, repeated-reference slice, and streaming source-order regressions.

**Acceptance gate:** The delayed-head fixture lowers `WindowBlockedMilliseconds` without increasing committed result disorder. Graph and persisted-shard contracts remain equal; peak buffered records stay below the configured budget.

### Task 6: Bound directory result backlog only if Phase 0 exposes it

**Files:**

- Modify: `src/Host/DeletionDirectoryAnalysisService.cs`
- Modify: `src/Host/Logging/AnalysisTextLogWriter.cs`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- Test: `tests/RoslynDeletionPrototype.HostTests/Logging/TextLogSystemTests.cs`

1. Write a host test that blocks the lowest-index file while later files complete. Assert separate timestamps/telemetry for file analysis completion and source-order publication.
2. Add directory telemetry for unpublished-result count, wait-to-publish milliseconds, and the oldest unpublished index. Capture memory at analysis completion as well as publication only when the benchmark filter permits it.
3. If the medium telemetry shows meaningful result retention, implement a bounded directory ordered-result window. New files may start only when the active plus unpublished result budget permits it.
4. Keep the final aggregation, rewrite-plan ordering, diff order, and user-visible completion contract source ordered. Do not emit duplicate `cat=file evt=completed` records.
5. Reject the directory-window change if it merely moves wall-clock from CPG work into scheduler wait without reducing heap/private-byte peaks or tail variance.

**Acceptance gate:** The directory backlog is either measured and bounded, or explicitly left unchanged with evidence that it does not contribute materially to the medium tail.

## Phase 3: Escalate Only When Admission Is Insufficient

### Task 7: Introduce fair asynchronous partition scheduling only when required

**Files:**

- Create: `src/MinimalRoslynCpg/Builder/CpgPartitionWorkScheduler.cs`
- Modify: `src/MinimalRoslynCpg/Builder/BoundedPartitionWorkWindow.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Test: `tests/RoslynDeletionPrototype.PerformanceTests/Concurrency/BoundedRuleStageSchedulerConcurrencyTests.cs`
- Test: `tests/RoslynDeletionPrototype.ContractTests/Cpg/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. Enter this task only when Phase 1 records significant CPG admission wait or starvation after a bounded policy, and medium variance remains above the agreed gate.
2. Write a failing fairness test with two files whose local DOPs both exceed the global budget. Assert round-robin or otherwise documented fair grant behavior, bounded active partitions, cancellation, and no blocked ThreadPool worker waiting only for a permit.
3. Add an asynchronous scheduler abstraction owned by the builder layer. It accepts per-file requested degree and globally schedules individual partition work; it must not make `MinimalRoslynCpg` depend on Host or Rules.
4. Convert only partition-window scheduling to await the shared scheduler. Keep Roslyn fact collection in workers and graph mutation in the stable ordered commit path.
5. Preserve a local per-file degree cap, global total budget, and the Phase 2 reorder-record limit. Report local queue wait, global budget wait, and fairness queue length separately.
6. Remove whole-build admission only after the partition scheduler satisfies all boundedness, cancellation, graph, and tail tests.

**Acceptance gate:** The fair scheduler reduces medium tail variance beyond Phase 1 without changing graph/rule/rewrite output or increasing peak memory above the accepted budget.

## Final Verification And Default Decision

After each task, run its owning focused tests. Before a real-source run, execute:

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~CpgShardBuildCoordinatorTests" -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~TextLogSystemTests" -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-build --filter "FullyQualifiedName~BoundedRuleStageSchedulerConcurrencyTests" -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

Run the small, medium, and mixed fixtures with one warmup plus at least three measured samples for `(12,1)`, `(1,12)`, and `(12,12)` plus the selected budget policy. Report median and range for wall-clock, CPG and rule phases, freeze substeps, ordered-window waits, allocation, GC, heap, working/private bytes, ThreadPool, completed-file count, and graph/rule/rewrite equivalence.

Schedule the 1,503-file matrix only when the medium gate passes. A default change requires stable benefits across fixtures, no semantic drift, no invalid samples, and no unexplained increase in peak memory or tail range. Otherwise, retain the current default and preserve the telemetry for the next diagnosis.

## Rollback

Each phase is independently reversible:

- Telemetry can remain after a scheduling change is reverted.
- Disable admission or fair scheduling by removing the optional builder budget while retaining the existing DOP path.
- Set reorder allowance to zero to restore the current ordered window.
- Revert directory-window admission independently of CPG changes.

Do not use a lower test budget, disabled memory snapshots, skipped graph-equivalence coverage, or a new default DOP to conceal a regression.
