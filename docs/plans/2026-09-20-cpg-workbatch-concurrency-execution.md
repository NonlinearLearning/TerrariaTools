# CPG WorkBatch Concurrency Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace function-level CPG partition scheduling with fixed long-lived workers that process cost-bounded WorkBatch instances and reduce immutable local CPG fragments without changing graph semantics.

**Architecture:** Keep Roslyn and the existing `NLCPGGraph` as the semantic baseline. Add a cost model and immutable WorkBatch contract, run batches through a bounded channel serviced by a fixed worker set, produce `LocalCpgFragment` values, and perform stable-anchor-based reduction and final graph publication behind explicit stage barriers. Preserve the current ordered path as the DOP 1 compatibility oracle until graph, query, rule, rewrite, and diff equivalence is proven.

**Tech Stack:** .NET 10, C# preview, Roslyn 4.14, `System.Threading.Channels`, existing `NLCPG` stable anchors and fragment descriptors, `BoundedConcurrencyPool`, xUnit contract tests, performance diagnostics, and the repository serial build wrapper.

---

## Working rules

- Work in `D:\ProjectItem\SourceCode\Net\NL` and preserve unrelated user changes already present in the worktree.
- Read `D:\TRbackup\NLTX\Context\约束\构建与验证约束.md` before any compile-capable command.
- Before every build or test, inspect active `dotnet.exe` and `csc.exe` processes. Do not terminate the existing Version4 export or any unclear owner.
- Every compile-capable command must go through `Build\Tools\Invoke-SerialDotnet.ps1` with `-m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:BuildInParallel=false`.
- The current ordered implementation and DOP 1 behavior are the semantic oracle. Do not delete it until equivalence tests pass and the migration task explicitly allows removal.
- Keep generated outputs under `Build\bin`, `Build\obj`, `Build\generated`, or a declared benchmark output directory. Do not write benchmark artifacts beside source files.

## Existing implementation anchors

- Builder orchestration: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Builder configuration: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`
- Syntax partitioning: `src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs` and `src/NLCPG/Builder/Passes/SyntaxPass.cs`
- Operation partitioning: `src/NLCPG/Builder/Passes/PartitionedOperationPass.cs`
- Data flow stages: `src/NLCPG/Builder/Passes/DataFlowPass.cs`
- Cross-method stages: `src/NLCPG/Builder/Passes/CallGraphPass.cs`, `MemberAccessPass.cs`, and `InterproceduralDataFlowPass.cs`
- Stable fragment types: `src/NLCPG/Builder/Streaming/CpgNodeDescriptor.cs`, `CpgEdgeCandidate.cs`, `FragmentOwnershipIndex.cs`
- Shared graph and final index: `src/NLCPG/Model/NLCPGGraph.cs`
- Concurrency infrastructure: `src/NLISSN.Infrastructure/Concurrency/IConcurrencyPool.cs` and `BoundedConcurrencyPool.cs`
- Existing contract tests: `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`, `CpgShardBuildCoordinatorTests.cs`, and `PartitionPerformanceDiagnosticsTests.cs`
- Design reference: `设计docs/目前设计/cpg-workbatch-concurrency.md`

## Task 1: Lock the WorkBatch cost contract

**Files:**
- Create: `src/NLCPG/Builder/Concurrency/CpgWorkItem.cs`
- Create: `src/NLCPG/Builder/Concurrency/CpgWorkBatch.cs`
- Create: `src/NLCPG/Builder/Concurrency/CpgWorkBatchCostModel.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchCostModelTests.cs`

**Step 1: Write the failing tests**

Cover:

- `cost = max(1, endLine - startLine + 1)`;
- empty or invalid spans use cost 1 and report the fallback reason;
- 1-40, 41-200, 201-800, and greater-than-800 line spans map to the intended size category;
- threshold and target/max batch cost values reject zero or negative configuration;
- source order and stable span identity do not depend on worker completion order.

**Step 2: Run the focused test to verify it fails**

Run from the repository root after checking build ownership:

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore --filter FullyQualifiedName~CpgWorkBatchCostModelTests -m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:BuildInParallel=false
```

Expected: FAIL because the WorkBatch types and cost model do not exist.

**Step 3: Implement the minimal contract**

Add immutable records for `CpgWorkItem`, `CpgWorkBatch`, cost estimate, size class, and configuration. Keep the first estimator line-based. Do not add syntax complexity weights yet.

**Step 4: Run the focused test to verify it passes**

Run the same command. Expected: PASS with no semantic changes to the builder.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Concurrency tests/NLISSN.ContractTests/Cpg/CpgWorkBatchCostModelTests.cs src/NLCPG/Builder/NLCPGBuilderOptions.cs
git commit -m "feat: define CPG work batch cost contract"
```

## Task 2: Add an immutable local fragment contract

**Files:**
- Create: `src/NLCPG/Builder/Concurrency/LocalCpgFragment.cs`
- Create: `src/NLCPG/Builder/Concurrency/CpgFragmentMetrics.cs`
- Modify: `src/NLCPG/Builder/Streaming/CpgNodeDescriptor.cs`
- Modify: `src/NLCPG/Builder/Streaming/CpgEdgeCandidate.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/LocalCpgFragmentContractTests.cs`

**Step 1: Write the failing tests**

Assert that a fragment:

- stores stable anchors and edge candidates, not final global `NodeId` allocation;
- can be ordered by batch stable order;
- contains no Roslyn object references in its public payload;
- preserves boundary references, diagnostics, actual node/edge counts, and fragment byte metrics;
- rejects an edge whose endpoint is not represented as a stable anchor or explicit boundary reference.

**Step 2: Run the focused test to verify it fails**

Run the serial wrapper with filter `FullyQualifiedName~LocalCpgFragmentContractTests`. Expected: FAIL because the fragment contract is absent.

**Step 3: Implement the minimal fragment DTOs**

Reuse `CpgNodeDescriptor` and `CpgEdgeCandidate` where their current shape already matches the contract. Add method summaries and boundary references only as data-only records. Make ownership and disposal explicit; do not make the fragment hold a `SemanticModel`, `SyntaxNode`, or `IOperation`.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Concurrency src/NLCPG/Builder/Streaming tests/NLISSN.ContractTests/Cpg/LocalCpgFragmentContractTests.cs
git commit -m "feat: add immutable local CPG fragments"
```

## Task 3: Implement cost-bounded WorkBatch assembly

**Files:**
- Create: `src/NLCPG/Builder/Concurrency/CpgWorkBatchBuilder.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchBuilderTests.cs`

**Step 1: Write the failing tests**

Use Roslyn test sources to assert:

- small methods are packed until the target cost without exceeding the hard cost where possible;
- a large method is not grouped with another large method;
- a method larger than the hard limit becomes an isolated oversized batch;
- declaration and file-prelude work has stable placement;
- nested local functions are not scheduled twice;
- changing input enumeration completion order does not change batch stable order.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgWorkBatchBuilderTests`. Expected: FAIL because batch assembly is not wired into the builder.

**Step 3: Implement the minimal assembler**

Discover outermost method roots from the existing operation-root discovery, calculate line costs from syntax spans, sort by source order, and pack using target/max cost plus method-count and estimated-bytes guards. Keep oversized methods intact. Do not split a method by arbitrary line count.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Concurrency src/NLCPG/Builder/NLCPGBuilder.cs src/NLCPG/Builder/NLCPGBuilderOptions.cs tests/NLISSN.ContractTests/Cpg/CpgWorkBatchBuilderTests.cs
git commit -m "feat: assemble cost bounded CPG work batches"
```

## Task 4: Add the fixed-worker bounded executor

**Files:**
- Modify: `src/NLISSN.Infrastructure/Concurrency/IConcurrencyPool.cs`
- Modify: `src/NLISSN.Infrastructure/Concurrency/BoundedConcurrencyPool.cs`
- Create: `src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs`
- Test: `tests/NLISSN.PerformanceTests/Concurrency/CpgWorkBatchExecutorContractTests.cs`

**Step 1: Write the failing tests**

Cover:

- exactly P long-lived worker loops are started for a build;
- each worker processes several batches without creating a task per batch or per function;
- a bounded queue applies backpressure when the fragment sink is slower than producers;
- DOP 1 preserves input order at the reducer boundary;
- cancellation completes the channel and does not accept new work;
- one worker failure is observed by the build and does not become an unreported partial success.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgWorkBatchExecutorContractTests`. Expected: FAIL because no batch executor exists.

**Step 3: Implement the minimal executor**

Use a bounded `Channel<CpgWorkBatch>`. Start one task per worker at executor start, loop over batches, and send completed fragments to an explicitly bounded sink. Integrate existing admission and telemetry hooks where available. Do not call `Task.Run` inside the worker loop. Preserve the existing `CommitOrdered` implementation for callers that have not migrated.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS, including cancellation and failure cases.

**Step 5: Commit**

```powershell
git add src/NLISSN.Infrastructure/Concurrency src/NLCPG/Builder/Concurrency tests/NLISSN.PerformanceTests/Concurrency/CpgWorkBatchExecutorContractTests.cs
git commit -m "feat: execute CPG batches with fixed workers"
```

## Task 5: Move syntax collection to batch workers

**Files:**
- Modify: `src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/NLCPG/Builder/Passes/SyntaxPass.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchSyntaxTests.cs`

**Step 1: Write the failing tests**

Add a batch-mode contract test that compares the existing ordered path and the new path for syntax nodes, declared/reference symbols, type facts, source spans, and stable anchors. Add a test proving a batch worker only returns facts and does not mutate `NLCPGGraph`.

**Step 2: Run the focused tests**

Run the serial wrapper with filters `FullyQualifiedName~CpgWorkBatchSyntaxTests|FullyQualifiedName~NLCPGPartitionedBuilderTests`. Expected: the new batch test fails while existing partition tests remain the oracle.

**Step 3: Implement syntax batch collection**

Adapt the current `AnalyzeSyntaxFacts` loop to consume several work items in one worker invocation and return a `LocalCpgFragment`. Keep any file-level nodes outside method roots in a deterministic prelude. Do not call `AddNode` or `AddEdge` from worker code.

**Step 4: Run the focused tests**

Run the same filters. Expected: PASS and identical syntax graph signature for DOP 1.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs src/NLCPG/Builder/Passes/SyntaxPass.cs src/NLCPG/Builder/NLCPGBuilder.cs tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs tests/NLISSN.ContractTests/Cpg/CpgWorkBatchSyntaxTests.cs
git commit -m "refactor: collect syntax facts by CPG batch"
```

## Task 6: Move operation collection and local materialization to fragments

**Files:**
- Modify: `src/NLCPG/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/NLCPG/Builder/Passes/OperationPass.cs`
- Modify: `src/NLCPG/Builder/Streaming/CpgNodeDescriptor.cs`
- Modify: `src/NLCPG/Builder/Streaming/CpgEdgeCandidate.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchOperationTests.cs`

**Step 1: Write the failing tests**

Compare operation tree shape, parent-child edge candidates, syntax-operation links, type edges, symbol-resolution candidates, and operation inventory under DOP 1, 2, and 16. Include a deep operation source to ensure the explicit stack remains in use.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgWorkBatchOperationTests`. Expected: FAIL for the new fragment-based assertions.

**Step 3: Implement batch operation collection**

Replace one-root-at-a-time scheduling with one batch invocation that continuously analyzes its operation roots and returns one fragment. Keep operation order inside the fragment. Move shared graph mutation into reducer code and preserve the current `OperationFragmentFacts` behavior until the new fragment equivalence test is green.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS for DOP 1, 2, and 16.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Passes/PartitionedOperationPass.cs src/NLCPG/Builder/Passes/OperationPass.cs src/NLCPG/Builder/Streaming tests/NLISSN.ContractTests/Cpg/CpgWorkBatchOperationTests.cs
git commit -m "refactor: collect operation facts by CPG batch"
```

## Task 7: Add the local reducer and stable graph publication

**Files:**
- Create: `src/NLCPG/Builder/Concurrency/CpgFragmentReducer.cs`
- Modify: `src/NLCPG/Model/NLCPGGraph.cs`
- Modify: `src/NLCPG/Builder/StableNodeIdentityFactory.cs`
- Modify: `src/NLCPG/Builder/Streaming/FragmentOwnershipIndex.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgFragmentReducerContractTests.cs`

**Step 1: Write the failing tests**

Assert that reducer output is identical when fragments arrive in every permutation, duplicate node descriptors collapse by stable anchor, duplicate edges collapse by the full edge key, and unresolved boundary endpoints remain unavailable instead of becoming guessed edges.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgFragmentReducerContractTests`. Expected: FAIL because fragment reduction is not available.

**Step 3: Implement deterministic reduction**

Sort descriptors by stable anchor, allocate or recover `NodeId` only in the reducer, resolve edge candidates after all local nodes for the stage are known, then submit to the existing graph. Call `FreezeQueryIndex()` exactly once after final publication. Keep the reducer single-writer initially; add partitioned reducers only after the single-writer contract is stable.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS for all fragment arrival permutations.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Concurrency src/NLCPG/Model/NLCPGGraph.cs src/NLCPG/Builder/StableNodeIdentityFactory.cs src/NLCPG/Builder/Streaming/FragmentOwnershipIndex.cs tests/NLISSN.ContractTests/Cpg/CpgFragmentReducerContractTests.cs
git commit -m "feat: reduce CPG fragments deterministically"
```

## Task 8: Batch local CFG, dominance, control dependence, and member facts

**Files:**
- Modify: `src/NLCPG/Builder/Passes/ControlFlowPass.cs`
- Modify: `src/NLCPG/Builder/Passes/DominancePass.cs`
- Modify: `src/NLCPG/Builder/Passes/ControlDependencePass.cs`
- Modify: `src/NLCPG/Builder/Passes/MemberAccessPass.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLocalPassTests.cs`

**Step 1: Write the failing tests**

For representative branch, loop, exception, property, indexer, and receiver sources, compare local CFG and member facts between the old ordered path and batched execution. Include a negative test for unresolved/dynamic access.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgWorkBatchLocalPassTests`. Expected: FAIL for the new batch path.

**Step 3: Implement local pass execution**

Schedule only method-local work in batches. Return CFG and member edge candidates in fragments; keep global symbol identity and unknown behavior unchanged. Make dominance/control-dependence depend on the completed method-local CFG barrier rather than on worker completion order.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS with unchanged capability and unknown semantics.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Passes/ControlFlowPass.cs src/NLCPG/Builder/Passes/DominancePass.cs src/NLCPG/Builder/Passes/ControlDependencePass.cs src/NLCPG/Builder/Passes/MemberAccessPass.cs tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLocalPassTests.cs
git commit -m "refactor: batch local CPG passes"
```

## Task 9: Batch data-flow collection and solving

**Files:**
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDataFlowTests.cs`
- Test: `tests/NLISSN.UnitTests/Performance/PerformanceFactsTests.cs`

**Step 1: Write the failing tests**

Cover independent methods, branch merges, loops, calls, property access, budget truncation, `SkipMethod`, `FailBuild`, and cancellation. Assert that `prepare` is not accidentally executed serially on the scheduling thread for all methods.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgWorkBatchDataFlowTests`. Expected: FAIL for batch telemetry and equivalence assertions.

**Step 3: Implement the batch data-flow stages**

Make collect and local solve consume several method plans per batch and return summaries/candidate edges. Keep `NLCPGDataFlowOptions` limits explicit; do not silently replace `Unbounded` defaults without a separate performance and semantic decision. Use a method barrier before interprocedural bridge generation.

**Step 4: Run the focused test**

Run the same filter and then the affected unit test filter. Expected: PASS, including overflow and unknown behavior.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Passes/DataFlowPass.cs src/NLCPG/Builder/NLCPGBuilderOptions.cs tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDataFlowTests.cs tests/NLISSN.UnitTests/Performance/PerformanceFactsTests.cs
git commit -m "refactor: batch CPG data flow work"
```

## Task 10: Add cross-fragment call graph and interprocedural barriers

**Files:**
- Modify: `src/NLCPG/Builder/Passes/CallGraphPass.cs`
- Modify: `src/NLCPG/Builder/Passes/InterproceduralDataFlowPass.cs`
- Modify: `src/NLCPG/Builder/Passes/InterproceduralDataFlowPlan.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchInterproceduralTests.cs`

**Step 1: Write the failing tests**

Cover same-file calls, cross-file calls, overloads, local functions/lambdas, unresolved calls, external targets, recursive calls, and the opt-in interprocedural capability. Assert that boundary edges are emitted only after their endpoints and call targets are known.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgWorkBatchInterproceduralTests`. Expected: FAIL for the new barrier assertions.

**Step 3: Implement the barrier**

Collect symbol keys and call-site summaries in worker fragments, reduce a deterministic global call index, then run interprocedural plan generation and bridge publication. Preserve existing `unknown`, `external`, and `summary mapping` labels. No worker may publish an interprocedural edge directly to the shared graph.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS for DOP 1, 2, and 16.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Passes/CallGraphPass.cs src/NLCPG/Builder/Passes/InterproceduralDataFlowPass.cs src/NLCPG/Builder/Passes/InterproceduralDataFlowPlan.cs src/NLCPG/Builder/NLCPGBuilder.cs tests/NLISSN.ContractTests/Cpg/CpgWorkBatchInterproceduralTests.cs
git commit -m "feat: add CPG cross fragment barriers"
```

## Task 11: Integrate backpressure, cancellation, and failure publication

**Files:**
- Modify: `src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`
- Modify: `src/NLCPG/Persistence/CpgShardBuildCoordinator.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLifecycleTests.cs`

**Step 1: Write the failing tests**

Cover queue capacity, estimated-cost watermarks, fragment sink pressure, cancellation before enqueue, cancellation during analysis, one failed batch, reducer failure, incomplete shard publication, and retry behavior.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgWorkBatchLifecycleTests`. Expected: FAIL for lifecycle behavior not yet wired through the builder and shard coordinator.

**Step 3: Implement lifecycle handling**

Complete the channel on cancellation, stop accepting new batches, drain or dispose local fragment buffers, propagate the first failure, and mark the build incomplete before any query-visible publication. Make cleanup idempotent and keep diagnostics separate from ECS evidence.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS with no leaked worker or pending fragment.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/Concurrency src/NLCPG/Builder/NLCPGBuilder.cs src/NLCPG/Builder/NLCPGBuilderOptions.cs src/NLCPG/Persistence/CpgShardBuildCoordinator.cs tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLifecycleTests.cs
git commit -m "feat: bound and cancel CPG work batches"
```

## Task 12: Add deterministic equivalence coverage

**Files:**
- Create: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDeterminismTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs`
- Modify: `tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs`

**Step 1: Write the failing tests**

For the same Roslyn source and project fixture, build with DOP 1, 2, and 16 and compare:

- graph signature including stable node anchors, node kinds, spans, labels, edge kinds, and edge labels;
- query results and frozen index output;
- CPG capabilities and diagnostics;
- representative rule decisions, rewritten source, and diff output;
- persisted shard/catalog routing and cross-shard boundary edges.

**Step 2: Run the focused test**

Run the serial wrapper with filter `FullyQualifiedName~CpgWorkBatchDeterminismTests`. Expected: FAIL until every stage uses stable reduction and explicit barriers.

**Step 3: Implement only the smallest determinism fix**

Fix unstable enumeration, identity allocation, edge ordering, or reducer timing one cause at a time. Do not sort only the final JSON as a workaround; the graph and query layer must be deterministic before serialization.

**Step 4: Run the focused test**

Run the same filter. Expected: PASS for all three DOP values.

**Step 5: Commit**

```powershell
git add tests/NLISSN.ContractTests/Cpg tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs
git commit -m "test: prove CPG work batch determinism"
```

## Task 13: Add performance telemetry and large-file benchmarks

**Files:**
- Modify: `src/NLCPG/Builder/PartitionPerformanceEvent.cs`
- Create: `tests/NLISSN.PerformanceTests/Cpg/CpgWorkBatchPerformanceTests.cs`
- Modify: `tests/NLISSN.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj`
- Create: `docs/benchmarks/cpg-workbatch-baseline.md`

**Step 1: Write the failing benchmark assertions**

Record baseline and WorkBatch runs for small, mixed, and large files. Assert only instrumentation invariants initially: worker count, batch count, no per-function task metric growth, queue high-water mark, fragment bytes, and stage timings. Do not assert an unmeasured speedup.

**Step 2: Run the focused benchmark test**

Run the serial wrapper for the affected performance project with filter `FullyQualifiedName~CpgWorkBatchPerformanceTests`. Expected: FAIL until batch telemetry is emitted.

**Step 3: Implement telemetry**

Add stable stage identifiers and records for batch cost, queue wait, collection, local solve, reducer wait, fragment bytes, active workers, and tail latency. Reuse the existing performance event sink instead of adding an unrelated logging channel.

**Step 4: Run the focused benchmark test**

Run the same filter and save output beneath the declared benchmark directory. Expected: PASS with DOP 1, 2, and 16 samples.

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/PartitionPerformanceEvent.cs tests/NLISSN.PerformanceTests/Cpg tests/NLISSN.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj docs/benchmarks/cpg-workbatch-baseline.md
git commit -m "perf: measure CPG work batch scheduling"
```

## Task 14: Run Version4 acceptance and update design documentation

**Files:**
- Modify: `设计docs/目前设计/cpg-workbatch-concurrency.md`
- Modify: `设计docs/目前设计/cpg-architecture.md`
- Modify: `设计docs/目前设计/日志与并发.md`
- Modify: `Context/feature_list.json` only if the project feature is already registered there
- Create or update: `docs/benchmarks/cpg-workbatch-version4.md`

**Step 1: Run focused contract tests**

Run the affected CPG contract test project serially through the wrapper with `--no-build --no-restore` after the affected project has been built. Record project, command, exit code, warning/error counts, and artifact path.

**Step 2: Build only affected projects**

Inspect active build processes, then build `src/NLCPG/NLCPG.csproj`, `src/NLISSN.Infrastructure/Concurrency/NL.Concurrency.csproj`, and the affected test project one at a time through `Invoke-SerialDotnet.ps1`. Verify outputs under `Build/bin`. Do not build the whole solution for this change.

**Step 3: Run the Version4 comparison**

Use the existing NLCPG/Version4 analysis entry point with DOP 1, 2, and 16 against the same source snapshot. Store output in a separate benchmark directory and do not overwrite the user’s existing export. Compare graph signature, query/rule/rewrite/diff equivalence, peak memory, freeze/catalog time, worker task count, queue wait, and P95/P99 tail latency.

**Step 4: Record the acceptance result**

Mark the design as implemented only if semantic equivalence, cancellation/failure behavior, and bounded memory are proven. If performance is neutral or worse, keep the WorkBatch path opt-in and record the reason; do not claim improvement from worker count alone.

**Step 5: Run documentation gates**

From `D:\TRbackup\NLTX`, run:

```powershell
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
```

Read back both CPG WorkBatch documents and inspect all changed links before reporting completion.

**Step 6: Commit**

```powershell
git add 设计docs/目前设计/cpg-workbatch-concurrency.md 设计docs/目前设计/cpg-architecture.md 设计docs/目前设计/日志与并发.md docs/benchmarks/cpg-workbatch-version4.md Context/feature_list.json
git commit -m "docs: record CPG work batch acceptance"
```

## Completion checklist

- [ ] Cost model and four size classes have contract tests.
- [ ] WorkBatch assembly is cost-bounded and stable.
- [ ] A build starts P long-lived workers, not one task per function.
- [ ] Queue and fragment memory are bounded.
- [ ] Syntax, operation, local CFG, local data flow, and member facts return fragments.
- [ ] Call graph and interprocedural passes run behind explicit barriers.
- [ ] Reducer owns NodeId allocation, edge deduplication, and final publication.
- [ ] `FreezeQueryIndex()` runs only after final graph mutation.
- [ ] DOP 1, 2, and 16 produce equivalent graph/query/rule/rewrite/diff results.
- [ ] Cancellation, failed batch, reducer failure, and incomplete shard publication are tested.
- [ ] Version4 baseline and WorkBatch telemetry are recorded without an unmeasured speedup claim.
- [ ] Harness consistency and `git diff --check` pass.
