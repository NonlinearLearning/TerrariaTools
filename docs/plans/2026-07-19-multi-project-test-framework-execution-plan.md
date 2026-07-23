# Multi-Project Test Framework Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Split the existing test project into Unit, Contract, Host, and Performance projects while preserving one shared, deterministic test-asset library and existing test behavior.

**Architecture:** `RoslynDeletionPrototype.Testing` is a non-test class library that owns reusable C# inputs, temporary-workspace materialization, text assertions, and failure-artifact helpers. The four test projects each reference it but never reference each other. Fast correctness checks run as normal `dotnet test` projects; full Terraria measurements remain an isolated PowerShell workflow outside the normal test runner.

**Tech Stack:** .NET 10, C# 13, xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.13.0, PowerShell 7, existing Roslyn/CPG projects.

---

## Scope and constraints

- Preserve existing test names, assertions, fixtures, and observable production behavior during the move.
- Reuse the current `TestCodeSet` and `TestInfrastructure` assets; do not introduce Verify, Coverlet, BenchmarkDotNet, Testcontainers, or another test runner in this migration.
- Do not put graph, Mark, Decision, Rewrite, or performance expectations in the input-asset project. Inputs and assertion oracles remain separate.
- Do not run Terraria as part of `dotnet test` or a pull-request workflow.
- Existing uncommitted file-classification and test-code-set-extraction changes are prerequisites. Complete and verify them before moving projects; do not repeat their moves in parallel.
- The repository is currently dirty. Keep commits limited to files owned by each completed task and do not stage unrelated work.

## Target layout

```text
tests/
  AGENTS.md
  RoslynDeletionPrototype.Testing/
    RoslynDeletionPrototype.Testing.csproj
    TestCodeSet/
    TestInfrastructure/
  RoslynDeletionPrototype.UnitTests/
    RoslynDeletionPrototype.UnitTests.csproj
  RoslynDeletionPrototype.ContractTests/
    RoslynDeletionPrototype.ContractTests.csproj
  RoslynDeletionPrototype.HostTests/
    RoslynDeletionPrototype.HostTests.csproj
  RoslynDeletionPrototype.PerformanceTests/
    RoslynDeletionPrototype.PerformanceTests.csproj
```

Dependency direction:

```text
UnitTests        ─┐
ContractTests    ─┼──> Testing
HostTests        ─┤
PerformanceTests ─┘

Test projects ──> the production projects they exercise
Test projects -X-> another test project
```

## Completion criteria

1. The four test projects build and run independently.
2. The shared project is not a test project and contains the only public `TestCodeSet` source assets.
3. The original tests have been moved exactly once, with the same aggregate test count and outcomes as the frozen baseline.
4. `UnitTests` has no `Host.csproj` reference; test projects have no references to another test project.
5. `Run-TestTiers.ps1` writes TRX and per-run evidence under `Build/TestResults/`.
6. `Run-PerformanceSuite.ps1` creates isolated warm-up and measurement logs, plus a median report, without being invoked by normal test commands.
7. Harness scripts, local instructions, and developer-facing test commands name the new projects rather than the retired monolithic project.

## Task 1: Freeze the current baseline and finish active prerequisites

**Files:**
- Test: `tests/RoslynDeletionPrototype.Tests/RoslynDeletionPrototype.Tests.csproj`

**Step 1: Complete classification and asset extraction before changing project boundaries.**

先将测试按领域目录归类，并把可复用的完整 C# 输入移入 `TestCodeSet`；这两个前置步骤只允许路径或 fixture 入口变化，不改变命名空间、断言、执行选项或生产行为。

**Step 2: Build and run the old test project to capture the migration baseline.**

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --logger "trx;LogFileName=pre-split-baseline.trx"
```

Expected: zero new build errors; record the exact test count, failures, warnings, duration, SDK, and commit SHA. Stop if the baseline has unexpected failures.

**Step 3: Commit the prerequisite work separately.**

Use a Lore message that states the move-only or asset-only scope and records the baseline result. Do not combine it with project splitting.

## Task 2: Create the shared test-asset project

**Files:**
- Create: `tests/RoslynDeletionPrototype.Testing/RoslynDeletionPrototype.Testing.csproj`
- Move: `tests/RoslynDeletionPrototype.Tests/TestCodeSet/` -> `tests/RoslynDeletionPrototype.Testing/TestCodeSet/`
- Move: `tests/RoslynDeletionPrototype.Tests/TestInfrastructure/TextDiffAssert.cs` -> `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/TextDiffAssert.cs`
- Move: `tests/RoslynDeletionPrototype.Tests/TestInfrastructure/BuildDiffArtifactWriter.cs` -> `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/BuildDiffArtifactWriter.cs`
- Create: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/TestWorkspace.cs`
- Create: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/TestWorkspaceWriter.cs`
- Create: `tests/RoslynDeletionPrototype.Testing/TestCodeSet/TestAsset.cs`
- Create: `tests/RoslynDeletionPrototype.Testing/TestCodeSet/TestAssetCatalog.cs`

**Step 1: Add a non-test library project.**

`RoslynDeletionPrototype.Testing.csproj` targets `net10.0`, enables implicit usings and nullable, and has `IsPackable=false`. It must not reference `Microsoft.NET.Test.Sdk`, `xunit`, or a test runner. Add only the production reference needed by the moved `TextDiffAssert` overloads; retain the existing `DiffDocument` behavior rather than changing assertions during this task.

**Step 2: Write failing catalog and workspace tests in the temporary old test project.**

Cover these public behaviors before moving the helper code:

- duplicate asset IDs are rejected;
- materializing the same multi-file asset twice creates distinct roots;
- output relative paths and file contents are stable;
- the temporary workspace is deleted when disposed.

**Step 3: Implement the smallest typed asset surface.**

Use a simple immutable input model:

```csharp
public sealed record TestAsset(
  string Id,
  string Domain,
  IReadOnlyDictionary<string, string> Files,
  IReadOnlyList<string> Tags);
```

`TestAssetCatalog` must enumerate assets in ordinal `Id` order. `TestWorkspaceWriter` must reject rooted paths and `..` segments before writing a file. Keep expected results out of this model.

**Step 4: Move existing public source assets without rewriting their text.**

Preserve existing `*Sources` namespaces and public constant names. Convert existing multi-file helpers into catalog assets only where multiple test projects will consume them; leave tiny one-off AST samples local to their test.

**Step 5: Verify the asset project and its existing coverage guard.**

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Testing\RoslynDeletionPrototype.Testing.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~TestCodeSetCoverageTests|FullyQualifiedName~TextAssertionUsageGuardTests"
```

Expected: the shared library builds and all catalog/coverage checks pass.

**Step 6: Commit only the shared-asset extraction.**

The Lore trailers must state that inputs moved without changing expected outputs and list the focused test evidence.

## Task 3: Add the four thin test projects and project-boundary checks

**Files:**
- Create: `tests/RoslynDeletionPrototype.UnitTests/RoslynDeletionPrototype.UnitTests.csproj`
- Create: `tests/RoslynDeletionPrototype.ContractTests/RoslynDeletionPrototype.ContractTests.csproj`
- Create: `tests/RoslynDeletionPrototype.HostTests/RoslynDeletionPrototype.HostTests.csproj`
- Create: `tests/RoslynDeletionPrototype.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj`
- Create: `tests/RoslynDeletionPrototype.ContractTests/TestProjectBoundaryTests.cs`

**Step 1: Create the four test project files with the current test package versions.**

Every test project keeps the existing `net10.0`, `ImplicitUsings`, `Nullable`, `LangVersion`, and xUnit package versions. Each references `RoslynDeletionPrototype.Testing.csproj`.

**Step 2: Give each project only the production references it needs.**

| Project | Allowed production references |
|---|---|
| UnitTests | `Application`, `Rules`, `MinimalRoslynCpg` |
| ContractTests | `Application`, `Rules`, `MinimalRoslynCpg`, `RoslynPrototype.Core` where required |
| HostTests | `Host`, `Application`, `Rules`, `MinimalRoslynCpg` |
| PerformanceTests | `Host`, `Application`, `Rules`, `MinimalRoslynCpg` |

`UnitTests` must not reference `Host.csproj`. No project references another project under `tests/`.

**Step 3: Write failing project-file contract tests.**

`TestProjectBoundaryTests` reads the five project files and asserts:

- `Testing` does not include test packages;
- all four test projects reference `Testing`;
- test-to-test project references are absent;
- `UnitTests` does not reference `Host`.

**Step 4: Build the empty projects and verify the architecture test turns green.**

```powershell
dotnet build .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build -p:UseSharedCompilation=false --filter FullyQualifiedName~TestProjectBoundaryTests
```

## Task 4: Move unit and contract tests one domain at a time

**Files:**
- Move from `tests/RoslynDeletionPrototype.Tests/Mark/` to `tests/RoslynDeletionPrototype.UnitTests/Mark/` where the test needs no host, directory, or persistence setup.
- Move from `tests/RoslynDeletionPrototype.Tests/Propagation/` to `tests/RoslynDeletionPrototype.UnitTests/Propagation/` where the test needs no host, directory, or persistence setup.
- Move from `tests/RoslynDeletionPrototype.Tests/Decision/` to `tests/RoslynDeletionPrototype.UnitTests/Decision/` where the test is rule-local.
- Move `tests/RoslynDeletionPrototype.Tests/Cpg/` to `tests/RoslynDeletionPrototype.ContractTests/Cpg/`.
- Move `tests/RoslynDeletionPrototype.Tests/Application/GraphAnalyzerTests.cs` to `tests/RoslynDeletionPrototype.ContractTests/Application/GraphAnalyzerTests.cs`.
- Move `tests/RoslynDeletionPrototype.Tests/Application/StructureViewBuilderTests.cs` to `tests/RoslynDeletionPrototype.ContractTests/Application/StructureViewBuilderTests.cs`.
- Move `tests/RoslynDeletionPrototype.Tests/TestCodeSetCoverageTests.cs` to `tests/RoslynDeletionPrototype.ContractTests/TestCodeSetCoverageTests.cs`.
- Move `tests/RoslynDeletionPrototype.Tests/Architecture/ArchitectureBoundaryTests.cs` to `tests/RoslynDeletionPrototype.ContractTests/Architecture/ArchitectureBoundaryTests.cs`.

**Step 1: Classify one test class by its actual dependencies before moving it.**

If a nominally unit-level test creates a `DeletionCommandHost`, writes a directory, opens a SQLite catalog, or persists shards, classify it as Host or Contract rather than forcing it into Unit.

**Step 2: Move one class and replace local source strings only with equivalent shared assets.**

Do not alter test names, options, or assertions in the same commit as the move.

**Step 3: Run the moved class in its new project.**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~<MovedTestClass>
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~<MovedTestClass>
```

Expected: the moved class has the same test count and result as the baseline.

**Step 4: Repeat by small domain batch and commit each stable batch.**

Keep CPG persistence, DOP graph-equivalence, and slice-query tests together because they share deterministic graph contracts.

## Task 5: Move host and performance tests while preserving external boundaries

**Files:**
- Move `tests/RoslynDeletionPrototype.Tests/Application/PipelineComponentTests.cs` to `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`.
- Move `tests/RoslynDeletionPrototype.Tests/Application/DeletionApplicationServiceFlowTests.cs` to `tests/RoslynDeletionPrototype.HostTests/Application/DeletionApplicationServiceFlowTests.cs`.
- Move `tests/RoslynDeletionPrototype.Tests/Logging/` to `tests/RoslynDeletionPrototype.HostTests/Logging/`.
- Move `tests/RoslynDeletionPrototype.Tests/Rewrite/RewritePlanPersistenceTests.cs` to `tests/RoslynDeletionPrototype.HostTests/Rewrite/RewritePlanPersistenceTests.cs`.
- Move host-dependent SQLite shard tests to `tests/RoslynDeletionPrototype.HostTests/Cpg/`.
- Move `tests/RoslynDeletionPrototype.Tests/TestInfrastructure/DeleteClassRandomSampleHelper.cs` and its tests to `tests/RoslynDeletionPrototype.HostTests/TestInfrastructure/`.
- Move `tests/RoslynDeletionPrototype.Tests/Performance/PerformanceOptimizationRegressionTests.cs` to `tests/RoslynDeletionPrototype.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`.

**Step 1: Write a host test proving every materialized input root is isolated.**

Run the same directory analysis twice through `TestWorkspaceWriter`; assert its diff, log, and rewrite-plan roots never overlap.

**Step 2: Move host tests in small groups and retain their real file-system behavior.**

Temporary test inputs must remain in each test's private root. Existing external sampled-source functionality continues to require an explicit source directory and must not run in the normal project test suite.

**Step 3: Keep small performance assertions functional, not threshold-driven.**

The `PerformanceTests` project continues to assert output and log consistency across DOP values. It may record timings through `ITestOutputHelper`; it must not fail because an absolute millisecond or allocation value changed.

**Step 4: Run host and performance project suites.**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false
```

## Task 6: Introduce tiered execution and per-run test evidence

**Files:**
- Create: `scripts/Run-TestTiers.ps1`
- Modify: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/BuildDiffArtifactWriter.cs`
- Create: `tests/RoslynDeletionPrototype.Testing/TestInfrastructure/TestRunArtifactContext.cs`
- Create: `tests/RoslynDeletionPrototype.ContractTests/TestInfrastructure/TestRunArtifactContextTests.cs`

**Step 1: Write failing artifact-context tests.**

They must prove that two generated run IDs produce distinct roots and that invalid path segments cannot escape `Build/TestResults`.

**Step 2: Make `BuildDiffArtifactWriter` require a run artifact context.**

Store generated diff text under `Build/TestResults/<run-id>/Diff/`. Preserve file naming and visible diff content. Remove reliance on one shared `Build/Diff/TestCodeSet` destination so parallel test jobs cannot overwrite each other.

**Step 3: Implement `Run-TestTiers.ps1`.**

The script accepts `-Fast`, `-Host`, `-Performance`, and `-All`. It sets `DOTNET_CLI_HOME`, executes the selected project commands serially, writes a TRX file for each project to the run root, emits `run.json` with git SHA, SDK, project, command, start/end time, and exit code, and exits on the first failed tier.

**Step 4: Verify tiered execution.**

```powershell
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\scripts\Run-TestTiers.ps1 -Host
```

Expected: each command writes TRX and `run.json` under a unique run root; all selected tests pass.

## Task 7: Add the isolated real-source performance runner

**Files:**
- Create: `scripts/Run-PerformanceSuite.ps1`
- Create: `scripts/tests/Run-PerformanceSuite.tests.ps1`
- Modify: `docs/developer-guide.md`
- Modify: `docs/harness-verification-matrix.md`

**Step 1: Write script tests using a fake runner and synthetic text logs.**

Cover DOP ordering, warm-up exclusion, three-sample median selection, missing completed-event failure, and peak extraction from sampled events.

**Step 2: Implement the runner parameters.**

Accept `-SourceRoot`, `-TargetName`, `-Dop`, `-WarmupCount`, `-MeasurementCount`, and `-OutputRoot`. Defaults are DOP 8/12/14/16, one warm-up, and three measured runs.

**Step 3: Launch one process per run with isolated logs.**

Use the existing RoslynPrototype CLI with `--skip-rewrite`, `--no-diff`, and a unique `--runtime-log` per run. Parse `cat=run evt=sampled` for memory and ThreadPool peaks and `cat=run evt=completed` for successful elapsed time.

**Step 4: Write machine-readable and reviewable reports.**

Create `summary.json`, `summary.csv`, and `summary.md`. Include source-root fingerprint, git SHA, SDK, OS, DOP, warm-up/measurement identity, elapsed median, working-set median peak, allocation, Gen2, ThreadPool peaks, and failure details.

**Step 5: Run the PowerShell script tests.**

```powershell
pwsh -File .\scripts\tests\Run-PerformanceSuite.tests.ps1
```

Expected: synthetic logs yield deterministic summaries. Do not run Terraria as part of this task unless the active feature explicitly requires new real-source measurements.

## Task 8: Retire the monolithic project and update repository surfaces

**Files:**
- Delete: `tests/RoslynDeletionPrototype.Tests/RoslynDeletionPrototype.Tests.csproj`
- Delete or move: remaining files under `tests/RoslynDeletionPrototype.Tests/`
- Create: `tests/AGENTS.md`
- Modify: `AGENTS.md`
- Modify: `scripts/check-harness-consistency.ps1`
- Modify: `scripts/harness-audit.ps1`
- Modify: `scripts/harness-classify-change.ps1`
- Modify: `docs/quick-start.md`
- Modify: `docs/developer-guide.md`
- Modify: `docs/contributing.md`
- Modify: `docs/harness-verification-matrix.md`
- Create: `.github/workflows/tests.yml` only if GitHub Actions is the selected CI platform.

**Step 1: Prove no source remains in the old project.**

Before deletion, enumerate its `*.cs` files and ensure every test is in exactly one new project. Compare the merged new-project test count with the frozen baseline.

**Step 2: Add `tests/AGENTS.md` and repoint root guidance.**

Document test naming, project ownership, the asset boundary, focused-to-full verification order, and the rule that external performance input is opt-in. Update root `AGENTS.md` to name `tests/AGENTS.md` rather than the retired project-local guide.

**Step 3: Update harness scripts and reader documentation.**

Replace hard-coded monolithic project commands with `Run-TestTiers.ps1` or the appropriate new project command. Keep quick-start examples short and point benchmark guidance to the developer guide.

**Step 4: Add CI only after local tier scripts pass.**

The initial workflow runs `-Fast` for pull requests and `-Host` on the default branch. Real-source performance requires an explicitly configured self-hosted Windows runner and remains disabled by default.

**Step 5: Run final verification.**

```powershell
dotnet build .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false
pwsh -File .\scripts\Run-TestTiers.ps1 -All
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

Expected: all selected test tiers pass, documentation and harness checks pass, and the final diff contains no whitespace errors.

## Learning directions after the split

- Keep xUnit as the runner; borrow its data-driven and isolated-test discipline without framework migration.
- Evaluate Verify only after structured CPG and rewrite outputs have a stable normalizer and reviewable accepted snapshots.
- Evaluate BenchmarkDotNet only for in-process CPG/query microbenchmarks; retain the PowerShell runner for whole-directory analysis.
- Add coverage collection before adding a coverage threshold; coverage is observability, not a proof of rule correctness.
- Evaluate Testcontainers only when a test truly needs an external service lifecycle that temporary directories and in-process SQLite cannot model.

## Risks and stop conditions

| Risk | Control / stop condition |
|---|---|
| Path moves alter test discovery or test count | Stop after every batch and compare moved-class results against the frozen baseline. |
| Shared project becomes a second application layer | Restrict it to inputs, workspace materialization, assertions, and evidence helpers. No production logic. |
| Host behavior leaks into unit tests | Enforce project-reference contract tests and move the test to Host when it creates a host, directory, or persistent catalog. |
| Snapshot or performance work hides regressions | Keep exact graph/rule/rewrite assertions and never use absolute timing assertions in normal tests. |
| Real-source benchmark becomes flaky CI | Keep it separate, include machine/source metadata, use warm-ups and medians, and require explicit runner configuration. |
