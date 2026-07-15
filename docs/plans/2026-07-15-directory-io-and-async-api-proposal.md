# Directory I/O and Async API Optimization Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use test-driven-development to implement this plan task-by-task.

**Goal:** Remove avoidable synchronous directory I/O and timing-log flush stalls, then expose an async analysis path without claiming a CPU-throughput improvement.

**Scope:** This proposal covers only the former P2 directory I/O/logging path and P3 caller-thread occupancy path. It does not change CPG graph ownership, rule semantics, default DOP, or the CPU-parallelization proposals already in progress.

**Tech Stack:** .NET 10, `System.Threading.Channels`, `File.ReadAllTextAsync`, `StreamWriter.WriteLineAsync`, xUnit, JSON Lines.

## Evidence and decision

The completed Terraria dry run processed 1,503 files at DOP 16 in 292.5 seconds. Its cumulative CPG and Mark times were 876,389 ms and 832,080 ms, while semantic-model time was 1,431 ms. Those cumulative times are worker sums, not wall-clock time, but they establish that the main workload is CPU-bound analysis rather than file I/O.

`DeletionDirectoryAnalysisService` reads every source with `File.ReadAllText` before parsing and creating the compilation. When phase logging is enabled, every completed file writes one record to each of seven JSONL files, takes a lock, and calls `StreamWriter.Flush`. For 1,503 files this is 10,521 synchronous flushes. The optional single total-timing log adds another 1,503 flushes.

`PartitionedSyntaxPass`, `PartitionedOperationPass`, and `DataFlowPass` wait synchronously on CPU worker tasks via `GetAwaiter().GetResult()`. Their workers run `Task.Run` bodies; replacing the wait with `await` can release an async caller thread but cannot reduce CPG CPU time on its own.

**Decision:** implement P2 first. Start P3 only after P2 proves that the async host boundary is useful to a real caller or CLI path. Keep synchronous public APIs as compatibility wrappers until all consumers have migrated. Do not change the default DOP based on this work.

## Compatibility constraints

- Directory analysis must still read the complete source set before constructing the compilation. No per-file semantic analysis may start from a partial compilation.
- Source-file ordering, parsed tree paths, diagnostics, rewrite results, and output order must remain unchanged.
- The CPG graph, builder dictionaries, sequence counters, and rule-stage materialization remain owned by the stable caller thread.
- A bounded I/O window is independent of the CPU DOP window. It must have an explicit upper bound and use cancellation.
- On normal completion, every enqueued timing record must be written before the command returns.
- Batched logging intentionally changes crash durability: records accepted after the last successful flush can be lost if the process terminates abruptly. This must be documented; disposal and command completion must force a final flush.
- No new package dependency is permitted. Use `System.Threading.Channels`, `File.ReadAllTextAsync`, `StreamWriter.WriteLineAsync`, and existing cancellation options.

## Proposed design

### P2A: bounded asynchronous source loading

Add an internal async directory-loading path in `DeletionDirectoryAnalysisService`.

1. Keep `EnumerateSourceFiles` and its ordinal sort unchanged.
2. Allocate a result slot for each ordered path.
3. Run at most `min(fileCount, configuredIoDop)` asynchronous readers. Each worker obtains the next ordinal, calls `File.ReadAllTextAsync(path, cancellationToken)`, and writes its string to that ordinal slot.
4. Reconstruct the existing `Dictionary<string, string>` in original path order only after all readers complete.
5. Parse trees and create the compilation exactly as today.

The initial I/O bound should be an internal constant or a narrowly scoped execution option, capped independently from CPU DOP. Do not create one waiting task per file and do not use the CPG scheduler for file I/O.

### P2B: one bounded asynchronous timing-log writer

Replace the per-file lock and immediate flush with one `Channel<TimingLogRecord>` and one writer task per directory analysis invocation.

- The producer emits a total record or a seven-phase record after a file result is complete; it does not open files or take a writer lock.
- The single consumer owns all enabled `StreamWriter` instances and writes JSONL in dequeue order. Completion order is permitted to differ from source order because the current parallel path already completes files out of order.
- Use a bounded channel. When the buffer is full, producer backpressure preserves memory bounds and prevents silent record loss.
- Flush after a bounded batch and again during orderly completion/disposal. The final completion awaits the writer task, then verifies every enabled stream has been flushed and disposed.
- If the writer faults, stop analysis through the shared cancellation path and surface the original I/O exception; do not continue with an incomplete timing log.

Add a small result metric for enqueued, written, and final-flush record counts. It proves normal-completion completeness without making log I/O part of CPG timing.

### P3: async caller boundary, staged after P2

Introduce async counterparts instead of changing every public method in place:

- `DeletionCommandHost.AnalyzeFromArgsAsync(...)` becomes the CLI-facing entry point; the existing synchronous entry remains a compatibility wrapper during migration.
- `DeletionDirectoryAnalysisService.AnalyzeDirectoryAsync(...)` awaits source loading, logging completion, and directory work.
- `DeletionApplicationService.AnalyzeAsync(...)` and `RoslynCpgBuilder.BuildAsync(...)` are added only when their callers can await them.

The partitioned syntax, operation, and data-flow passes may then await their existing worker-window tasks rather than calling `GetAwaiter().GetResult()`. Converting pass contracts must be all-or-nothing for a pipeline branch: do not leave a new async API that blocks internally at the same boundary.

The synchronous compatibility wrapper may block only at the outermost boundary and must be marked as legacy. `Program.Main` should await the async command path. This isolates the compatibility cost and avoids introducing a mixed sync/async call chain into graph materialization.

## Implementation sequence

### Task 1: lock down current directory and logging behavior

**Files:**

- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/Concurrency/BoundedRuleStageSchedulerConcurrencyTests.cs` only if an existing reusable bounded-window probe fits
- Inspect: `src/Host/DeletionDirectoryAnalysisService.cs`

1. Add a directory fixture containing multiple ordered source files.
2. Assert baseline result equivalence with phase logging disabled and enabled: marks, decisions, edits, diagnostics, rewritten source, and analyzed-file count must match.
3. Assert the seven phase files each contain exactly one JSON object per analyzed file after normal completion.
4. Add a cancellation/fault fixture that proves no command reports success after the log writer fails.

### Task 2: implement and test bounded async reads

**Files:**

- Modify: `src/Host/DeletionDirectoryAnalysisService.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. Add an injectable internal file-reader seam only if required to deterministically control delayed reads and cancellation. Do not expose it in the public CLI contract.
2. Write the failing test: delayed reads at a configured I/O window must never exceed that window and must preserve source-path order.
3. Add `ReadSourcesAsync` using a bounded worker loop and `File.ReadAllTextAsync`.
4. Await all reads before `ParseTrees` and compilation creation.
5. Verify directory analysis matches the synchronous baseline at I/O DOP 1 and a multi-reader value.

### Task 3: implement and test the bounded log writer

**Files:**

- Modify: `src/Host/DeletionDirectoryAnalysisService.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. Write failing normal-completion tests for all seven phase files and the optional total file. Assert exact record counts and JSON fields, not write order.
2. Add the channel-backed writer, its bounded capacity, batch flush policy, completion drain, and final flush.
3. Write a failing writer-fault test using an injectable internal stream/writer factory if a real filesystem failure cannot be produced deterministically.
4. Verify the writer propagates its exception, drains successfully on normal completion, and produces no duplicate records.

### Task 4: migrate the host to async without changing analysis semantics

**Files:**

- Modify: `src/Host/DeletionCommandHost.cs`
- Modify: `src/Host/DeletionDirectoryAnalysisService.cs`
- Modify: `src/RoslynPrototype/Program.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. Add async command and directory entry points.
2. Keep the current synchronous public entry point as an outer compatibility wrapper.
3. Make `Program.Main` await the async command entry point.
4. Add CLI-equivalence tests for single-file and directory inputs, including `--per-file-timing-log` and `--per-file-phase-timing-log-directory`.

### Task 5: decide whether full P3 propagation is justified

**Files:**

- Potentially modify: `src/Application/DeletionApplicationService.cs`
- Potentially modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Potentially modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedSyntaxPass.cs`
- Potentially modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedOperationPass.cs`
- Potentially modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. Measure whether an async host caller is blocked by the remaining synchronous partition waits in a representative run.
2. If no caller-level responsiveness requirement exists, stop here and retain the synchronous CPG build API.
3. If evidence supports it, add a coherent async pass contract and replace all three internal `GetAwaiter().GetResult()` waits with `await`.
4. Run complete graph snapshots at DOP 1, 8, 12, 14, and 16. No node, edge, ordering, type, CFG, DataFlow, decision, or edit difference is acceptable.

## Measurement and acceptance criteria

1. Directory output and graph/decision results are equal with async loading disabled and enabled.
2. A normal completed run writes exactly one record per analyzed file to every enabled timing log.
3. No file-reader or log-writer task exceeds its configured bounded window.
4. Cancellation and writer exceptions prevent a successful result and leave no background writer active after the command completes.
5. Async source loading and log writing do not regress the no-log directory wall-clock median across three comparable runs. A speedup is welcome but not required because CPG and Mark remain the dominant CPU cost.
6. P3 may claim only caller-thread availability. It must not claim CPG CPU throughput improvement without separate repeated wall-clock evidence.
7. Existing DOP graph-equivalence coverage remains green; default DOP and graph ownership are unchanged.

## Risks and stop conditions

| Risk | Control / stop condition |
| --- | --- |
| Partial source set changes compilation semantics | Await all ordered reads before parsing or creating the compilation. |
| Async reads over-queue slow storage | Bound I/O independently and test the active-reader maximum. |
| Batched log data is lost on abnormal process termination | Document the reduced crash durability; force final drain and flush on normal completion. |
| Writer fault becomes unobserved background failure | Await the consumer during completion and fail the command with its original exception. |
| Sync-over-async moves rather than disappears | Permit it only in the legacy outer wrapper; reject inner pipeline blocking in the P3 branch. |
| Async conversion alters graph determinism | Stop on any DOP snapshot difference; workers may not write graph or builder shared state. |

## Out of scope

- Parallel graph materialization, SyntaxPass restructuring, DataFlow candidate generation, and rule-level optimization.
- Changing the default CPU DOP or introducing a dedicated thread pool.
- Claiming that `async` makes Roslyn semantic queries or CPG construction execute faster.
