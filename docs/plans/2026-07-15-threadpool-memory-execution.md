# ThreadPool and Memory Control Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use test-driven-development to implement this plan task-by-task.

**Goal:** Reduce analysis-time task fan-out, transient allocations, and peak memory while retaining the existing shared .NET ThreadPool execution model and stable graph output.

**Architecture:** Parallel paths continue to submit work through `Task.Run` or `Parallel.ForEach` to the shared ThreadPool. A bounded worker window replaces per-partition task fan-out, and ordered merge releases each partition result as soon as it is materialized. `ArrayPool<T>` is restricted to worker-local buffers; graph nodes, edges, Roslyn symbols, and cross-stage records remain normally owned managed objects.

**Tech Stack:** .NET 10, `Task`, `SemaphoreSlim`, `ArrayPool<T>`, `System.Threading.ThreadPool`, xUnit, JSON Lines runtime metrics.

---

## Baseline evidence

The full Terraria directory `D:\lodes\TR\Backup\New1.27\1.45 2\TR` was analyzed serially by DOP with `--skip-rewrite --no-diff`. The new runtime log is `Build/terraria-dop-runtime-metrics-20260715-8-12-14-16.jsonl`; it records one sample every five seconds and one completed record per run.

| DOP | Completed elapsed | Allocated bytes | Peak working set | Peak ThreadPool threads | Peak pending work items |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 8 | 315.908s | 44.70 GiB | 7.85 GiB | 56 | 395 |
| 12 | 303.955s | 45.15 GiB | 9.10 GiB | 44 | 157 |
| 14 | 310.727s | 45.00 GiB | 9.00 GiB | 44 | 18 |
| 16 | 335.472s | 45.07 GiB | 9.14 GiB | 44 | 29 |

This is one run per DOP. It shows DOP 12 is the current fastest observed point, but it does not authorize changing the default DOP until three isolated repetitions confirm the median result.

Current sources of task and allocation pressure:

1. `BoundedRuleStageScheduler` allocates one waiting task per item before `SemaphoreSlim` permits work.
2. `PartitionedSyntaxPass`, `PartitionedOperationPass`, and two `DataFlowPass` partition methods each construct a task array for every partition.
3. Syntax, operation, and data-flow workers use `ToArray()` and `ToList()` for short-lived traversal and fact collections.
4. `Task.WhenAll(...)` retains every partition result until all work is complete; stable graph materialization happens later on the caller thread.
5. Graph materialization, shared dictionary updates, edge de-duplication, rewriting, and final result collection remain intentionally serial and must not be sent to concurrent graph writers.

## Compatibility constraints

- Continue using the shared .NET ThreadPool. Do not introduce a dedicated worker-thread pool.
- `SemaphoreSlim` remains a concurrency limit; it must not be described as a thread allocator.
- Worker code may only read `SemanticModel`, syntax, operation, and frozen method-local snapshots.
- A single stable materialization point owns graph node IDs, shared maps, edge de-duplication, and output ordering.
- Pooled storage must be returned in `finally`; no pooled array may be retained by a graph node, a result, a cache, or a Roslyn object.
- Complete graph, type, CFG, DataFlow, decision, and edit results must remain equal at DOP `1`, `8`, `12`, `14`, and `16`.

### Task 1: Lock down worker-window behavior

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/Concurrency/BoundedRuleStageSchedulerConcurrencyTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Modify: `src/RoslynPrototype/RuleServices/ExecutionRuntime.cs`

**Step 1: Write the failing tests**

Add a scheduler probe for 100 work items at DOP 4. Assert at most four work bodies are active and no more than four work bodies have been started before the first completion release. Add a CPG partition probe with the same window requirement.

**Step 2: Verify red**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~BoundedRuleStageSchedulerConcurrencyTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests"
```

Expected: the new assertions fail because current code creates all waiting tasks immediately.

**Step 3: Implement the bounded worker loop**

Replace task-per-item scheduling with exactly `min(itemCount, DOP)` worker tasks that pull the next index atomically and store results in their deterministic index slot. Preserve cancellation propagation and result ordering.

**Step 4: Verify green**

Re-run the focused tests. Confirm result order and DOP equivalence remain unchanged.

### Task 2: Apply the worker window to CPG partitions

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

**Step 1: Write failing DOP determinism tests**

Run existing complete graph snapshots at DOP `1`, `8`, `12`, `14`, and `16`, including a fixture with many method roots. Assert all node and edge snapshots are equal.

**Step 2: Implement one shared window helper per pass**

Each pass starts at most DOP worker tasks and writes results into preallocated ordinal slots. Do not merge results from workers concurrently.

**Step 3: Verify green**

Run the focused builder tests and retain current telemetry semantics: reported DOP is configured capacity, not a claim that exactly that many OS threads were created.

### Task 3: Remove avoidable transient collections and pool only worker-local buffers

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

**Step 1: Write allocation-oriented regression coverage**

Add a repeated-build test that verifies graph equality and records only relative allocation evidence. Do not assert an absolute byte value because Roslyn and runtime versions affect it.

**Step 2: Implement the smallest safe allocation reductions**

Replace child `ToArray()` traversal with direct reverse-index traversal where the API permits. Use `ArrayPool<T>` only for temporary stack or ordinal buffers whose elements are cleared before return when they contain references. Return rented buffers in `finally`.

**Step 3: Verify green**

Run DOP determinism and data-flow tests. Confirm no pooled array escapes the worker boundary.

### Task 4: Window ordered materialization and release results early

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

**Step 1: Write the failing release-order test**

Use a controllable partition probe that completes out of source order. Assert materialization stays source ordered and completed earlier partitions are released after their turn is materialized.

**Step 2: Implement the bounded reorder buffer**

Keep only the DOP-sized worker window plus the minimal out-of-order completion buffer. Materialize the next ordinal result immediately, clear its slot, and return any temporary rented storage.

**Step 3: Verify green**

Run complete graph equality at every accepted DOP and ensure no graph writer runs on a worker.

### Task 5: Repeat Terraria measurements and make the DOP decision

**Files:**
- Modify: `docs/quick-start.md`
- Modify: `progress.md`
- Modify: `feature_list.json`

**Step 1: Run isolated repetitions**

For each DOP `8`, `12`, `14`, and `16`, run the Terraria directory three times serially with a new JSONL file per repetition:

```powershell
dotnet run --no-build --project .\src\RoslynPrototype\RoslynPrototype.csproj -- `
  'D:\lodes\TR\Backup\New1.27\1.45 2\TR' `
  --target-name PlayerInput `
  --max-degree-of-parallelism <dop> `
  --runtime-metrics-log .\Build\terraria-dop-<dop>-run-<n>.jsonl `
  --skip-rewrite `
  --no-diff
```

**Step 2: Compare medians**

For each DOP, report median completed elapsed time, peak working set, peak ThreadPool thread count, peak pending work items, total allocated bytes, and Gen2 count.

**Step 3: Select the default only when evidence supports it**

Change the default from `Environment.ProcessorCount` only if one DOP has a lower median wall-clock than 16 and does not raise median peak working set. Otherwise preserve the current default and expose the measured recommendation in documentation.

## Acceptance criteria

1. No path creates one waiting task per partition or scheduler item; worker task count is bounded by DOP.
2. All existing graph and pipeline equivalence tests pass at DOP `1`, `8`, `12`, `14`, and `16`.
3. Terraria runtime logs contain five-second running records and exactly one completion record per run.
4. Median DOP selection is based on three isolated runs, never a single run.
5. The selected configuration has no median wall-clock regression versus DOP 16 and no peak-memory regression.
6. No pooled object remains reachable after a worker finishes or changes graph output semantics.

## Risks and stop conditions

| Risk | Control / stop condition |
| --- | --- |
| Nested directory and CPG parallelism over-queues the shared pool | Measure pending work items; do not raise DOP before worker-window conversion. |
| Pooled references remain live | Clear reference arrays and return in `finally`; stop if leak or stale-record tests fail. |
| Reordering changes node IDs or edge order | Keep source-order merge on one materializer; stop on any snapshot difference. |
| Lower allocation increases CPU due to copying | Retain a change only when repeated wall-clock and allocation data support it. |
| Timer sampling changes results slightly | Compare only runs with the same runtime logging configuration. |
