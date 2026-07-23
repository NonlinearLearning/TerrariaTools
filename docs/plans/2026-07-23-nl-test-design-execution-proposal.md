# NL Test Design Execution Proposal

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Turn the NL test design into tiered, reproducible semantic evidence while keeping xUnit as the primary runner.

**Architecture:** Complete the existing test-project split before introducing new test libraries. Add one canonical equivalence surface, use it for finite DOP and persistence matrices, then add constrained random generation and failure injection. Snapshot, benchmark, concurrency, and mutation tools stay isolated so they cannot redefine semantic correctness or slow normal test runs.

**Tech Stack:** .NET 10, C# 13, xUnit, `Xunit.Combinatorial`, FsCheck, Verify, BenchmarkDotNet, Microsoft Coyote, Stryker.NET, existing Roslyn CPG and persistence APIs.

---

## Preconditions and boundaries

- [NL Test Design](2026-07-23-directory-io-log-performance-tests-design.md) is the design authority.
- [Multi-Project Test Framework Implementation Plan](2026-07-19-multi-project-test-framework-execution-plan.md) must complete its frozen baseline, shared asset library, four test projects, and tier runner before Tasks 2 through 8 begin.
- Do not add a package while the test-project move is still changing test count or discovery.
- `xUnit` remains the only primary runner. This proposal does not migrate to NUnit or TUnit.
- Terraria runs remain outside `dotnet test`, CI pull requests, and routine local verification.
- Every new test must preserve the input/oracle split: `RoslynDeletionPrototype.Testing` owns inputs and test infrastructure; the owning test project owns expected graph, rule, rewrite, and performance outcomes.

## Phase 0: establish the split baseline

### Task 1: Finish and verify the existing project split

**Files:**
- Execute: `docs/plans/2026-07-19-multi-project-test-framework-execution-plan.md`
- Verify: `tests/RoslynDeletionPrototype.UnitTests/`
- Verify: `tests/RoslynDeletionPrototype.ContractTests/`
- Verify: `tests/RoslynDeletionPrototype.HostTests/`
- Verify: `tests/RoslynDeletionPrototype.PerformanceTests/`
- Verify: `scripts/Run-TestTiers.ps1`

**Step 1: Freeze the current monolithic result.**

Run:

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --logger "trx;LogFileName=pre-split-baseline.trx"
```

Expected: record the exact count, failures, warnings, elapsed time, SDK, and git SHA. Stop on a new failure.

**Step 2: Execute the split plan without adding new frameworks.**

Move the shared assets, create the four projects, and move tests by dependency as specified in the prerequisite plan.

**Step 3: Verify tier behavior.**

Run:

```powershell
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\scripts\Run-TestTiers.ps1 -Host
pwsh -File .\scripts\Run-TestTiers.ps1 -Performance
```

Expected: every command writes a distinct TRX and `run.json` under `Build/TestResults/`; the combined test count matches the frozen baseline.

**Step 4: Commit the completed split separately.**

Do not combine project moves with test-framework packages or changed semantic assertions.

## Phase 1: make semantic equivalence reusable

### Task 2: Add a canonical equivalence comparator

**Files:**
- Create: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/CpgExecutionSnapshot.cs`
- Create: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/CpgExecutionSnapshotComparer.cs`
- Test: `tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgExecutionSnapshotComparerTests.cs`
- Modify: `tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`
- Modify: `tests/RoslynDeletionPrototype.ContractTests/Application/GraphAnalyzerTests.cs`

**Step 1: Write failing comparer tests.**

Create snapshots that differ by exactly one contract at a time: graph edge, direct mark, propagated mark, decision/diagnostic, and rewrite diff. Assert the comparer names the failing contract and emits a stable sorted difference.

**Step 2: Run the focused tests.**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~CpgExecutionSnapshotComparerTests
```

Expected: fail because the comparer types do not exist.

**Step 3: Implement the smallest canonical snapshot.**

`CpgExecutionSnapshot` contains only observable values: graph snapshot version, sorted required node/edge projection, sorted direct/propagated marks, sorted decisions/diagnostics, rewritten source, and diff text. Do not include temporary paths, elapsed time, process IDs, or unordered dictionary enumeration.

**Step 4: Replace duplicate comparison blocks in two existing contract tests.**

Keep fixture-specific exact edge assertions. The common comparer supplements them; it does not weaken them to a hash-only comparison.

**Step 5: Re-run focused and contract tiers.**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgExecutionSnapshotComparerTests|FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~GraphAnalyzerTests"
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
```

Expected: comparer failures identify one stable semantic field; existing focused contracts remain unchanged in outcome.

**Step 6: Commit the comparator and its tests.**

## Phase 2: cover finite execution configurations

### Task 3: Introduce the DOP and persistence matrix

**Files:**
- Modify: `tests/RoslynDeletionPrototype.ContractTests/RoslynDeletionPrototype.ContractTests.csproj`
- Create: `tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgExecutionMatrixTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Testing/TestCodeSet/Cpg/`

**Step 1: Add one package.**

Add `Xunit.Combinatorial` only to `RoslynDeletionPrototype.ContractTests`. Pin its version with the project's package-version convention.

**Step 2: Write a failing matrix test.**

Use a small named CPG fixture with control flow, repeated references, and a call edge. Cover DOP 1, 4, 8, and 16; persistence off/on; Strict/Throughput; and configured writer concurrency. The serial in-memory snapshot is the baseline.

**Step 3: Run the test and inspect the generated case names.**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~CpgExecutionMatrixTests --logger "console;verbosity=detailed"
```

Expected: the initial test fails until it calls the canonical comparator for every selected row.

**Step 4: Implement test-only matrix helpers.**

Materialize a private store root per matrix row. Preserve the DOP, durability mode, writer concurrency, and fixture ID in the failure message and run artifact metadata.

**Step 5: Run the contract tier.**

Run:

```powershell
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
```

Expected: every selected row is graph, rule, decision, and rewrite equivalent to the serial baseline.

**Step 6: Commit the package and matrix together.**

## Phase 3: add constrained random semantic tests

### Task 4: Add reproducible FsCheck fixture generation

**Files:**
- Modify: `tests/RoslynDeletionPrototype.ContractTests/RoslynDeletionPrototype.ContractTests.csproj`
- Create: `tests/RoslynDeletionPrototype.Testing/TestCodeSet/Cpg/GeneratedCSharpFixture.cs`
- Create: `tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgPropertyEquivalenceTests.cs`
- Create: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/FailureArtifactWriter.cs`

**Step 1: Add `FsCheck.Xunit`.**

Pin it in the contract-test project. Do not add a generator that emits arbitrary Roslyn syntax trees.

**Step 2: Write failing fixed-seed tests.**

Begin with generators for logical expressions, member access, local methods, named and optional arguments, delegates, indexers, and two-file references. Generate valid source text only.

**Step 3: Implement the generator and artifact writer.**

On failure, write the effective seed, all input files, options, canonical snapshots, and diff under the unique `Build/TestResults/<run-id>/` root. Use ordinal ordering and reject rooted or parent-traversal paths.

**Step 4: Run each fixed seed first, then the configured property count.**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~CpgPropertyEquivalenceTests
```

Expected: a failing seed leaves a self-contained replay artifact; passing cases compare serial, selected DOP, and persistence recovery.

**Step 5: Commit generators, artifacts, and property tests together.**

## Phase 4: make persistence failure behavior deterministic

### Task 5: Add `CpgPersistenceTestKit`

**Files:**
- Create: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/CpgPersistenceTestKit.cs`
- Create: `tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgPersistenceStateTests.cs`
- Modify: `src/MinimalRoslynCpg/Builder/CpgShardBuildSession.cs`
- Modify: `src/MinimalRoslynCpg/Persistence/`

**Step 1: Write failing state tests.**

Cover staging visibility, completed-catalog visibility, write failure cleanup, cancellation cleanup, and cancellable store-lock timeout. Each test uses its own temporary root.

**Step 2: Add the narrowest internal test seam.**

Extend existing build-session checkpoints or writer hooks. Do not expose a public production testing API, use timer sleeps, or add global mutable test state.

**Step 3: Implement a typed fault schedule.**

The test kit controls when a write starts, fails, completes, or observes cancellation. It records state transitions for assertion.

**Step 4: Run the focused persistence suite.**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgPersistenceStateTests|FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~SqliteCpgShardCatalogTests"
```

Expected: no failed or cancelled build appears as completed, and every temporary root can be reopened after test disposal.

**Step 5: Commit the test seam and state tests separately from performance tuning.**

## Phase 5: add reviewed representations and benchmarks

### Task 6: Add canonical snapshots with Verify

**Files:**
- Modify: `tests/RoslynDeletionPrototype.ContractTests/RoslynDeletionPrototype.ContractTests.csproj`
- Create: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/CpgSnapshotNormalizer.cs`
- Create: `tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgReviewedSnapshotTests.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Rewrite/RewritePlanPersistenceTests.cs`

**Step 1: Write normalizer tests.**

Prove collection order is canonical and temporary paths, timestamps, and elapsed-time values are absent.

**Step 2: Add Verify and only three snapshots.**

Choose one complex graph fragment, one rewrite plan, and one catalog manifest. Keep their exact contract tests intact.

**Step 3: Verify review workflow.**

Run the focused test, inspect the received diff, then accept only deliberate baseline files. Do not use snapshot acceptance to resolve a semantic failure.

**Step 4: Commit normalizer, approved snapshots, and tests together.**

### Task 7: Add in-process microbenchmarks

**Files:**
- Create: `tools/CpgMicrobenchmarks/CpgMicrobenchmarks.csproj`
- Create: `tools/CpgMicrobenchmarks/Program.cs`
- Create: `tools/CpgMicrobenchmarks/CpgPersistenceBenchmarks.cs`
- Modify: `docs/developer-guide.md`

**Step 1: Add BenchmarkDotNet only to the new executable project.**

**Step 2: Benchmark shard export, serialization, catalog batch write, and slice query separately.**

Use fixed in-repository fixtures. Do not invoke directory CLI analysis from a microbenchmark.

**Step 3: Run a local report and record environment metadata.**

Run:

```powershell
dotnet run --project .\tools\CpgMicrobenchmarks\CpgMicrobenchmarks.csproj -c Release -- --filter "*"
```

Expected: a BenchmarkDotNet report with environment data; no comparison against the existing whole-pipeline persistence benchmark.

**Step 4: Commit benchmark project and documentation.**

## Phase 6: scheduled test-strength work

### Task 8: Evaluate Coyote and Stryker.NET in isolated scopes

**Files:**
- Create: `tests/RoslynDeletionPrototype.ContractTests/Concurrency/CpgSchedulerCoyoteTests.cs`
- Create: `scripts/Run-MutationTests.ps1`
- Modify: `docs/developer-guide.md`

**Step 1: Create one Coyote proof of concept.**

Model only the bounded scheduler, persistence writer lock, and cancellation callbacks. Keep the proof only when it produces a schedule that existing controlled tests cannot cover.

**Step 2: Configure mutation testing for a narrow target.**

Start with `src/Rules` and decision/rewrite conflict code. Exclude generated fixtures, host I/O, persistence files, and performance projects from the initial mutation run.

**Step 3: Run scheduled verification without introducing a coverage threshold.**

Run:

```powershell
pwsh -File .\scripts\Run-MutationTests.ps1
```

Expected: the report identifies surviving mutants and the follow-up is a focused semantic test, not a raw coverage target.

**Step 4: Commit the retained Coyote scope and mutation script independently.**

## Completion evidence

- The four test projects preserve the baseline test count and outcomes.
- Matrix and property tests prove the four semantic contracts across selected DOP, persistence, and durability configurations.
- Persistence state tests prove cleanup and catalog visibility under controlled failure and cancellation.
- All normal test tiers remain free of machine-dependent time thresholds.
- Benchmark reports and warmed Terraria medians are stored as distinct evidence; neither can override an equivalence failure.
- `pwsh -File .\scripts\check-harness-consistency.ps1` and `git diff --check` pass after each documentation or harness change.

## Risks and stop conditions

| Risk | Stop condition and response |
| --- | --- |
| Project split changes discovery | Stop framework work; restore test-count equivalence first. |
| Matrix runtime becomes excessive | Keep boundary rows and add full Cartesian coverage only for demonstrated defects. |
| Random generation produces invalid or irreducible failures | Restrict generators to named rule shapes and require seed artifacts. |
| Test seam changes production behavior | Keep seam internal, deterministic, and covered by state tests before expanding it. |
| Snapshot churn obscures semantics | Keep exact contract assertions and reject broad snapshot updates. |
| Benchmark result conflicts with semantic tests | Preserve the semantic result and investigate performance separately. |
