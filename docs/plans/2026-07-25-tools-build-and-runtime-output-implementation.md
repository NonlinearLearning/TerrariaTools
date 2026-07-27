# Tool Build And Runtime Output Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Keep tool compilation and default runtime reports in `Build/tools/`.

**Architecture:** Preserve the central MSBuild output rule. Give each runtime
tool a deterministic default report root under its tool-specific build output
directory, with explicit caller-supplied paths still taking precedence.

**Tech Stack:** .NET 10, C#, PowerShell, BenchmarkDotNet, xUnit.

---

### Task 1: Lock persistence-report path behavior

**Files:**
- Modify: `tests/Roslyn Prototype.PerformanceTests/Cpg/CpgPersistenceBenchmarkConfigurationTests.cs`
- Modify: `tools/CpgPersistenceBenchmark/BenchmarkConfiguration.cs`
- Modify: `tools/CpgPersistenceBenchmark/Program.cs`

**Step 1:** Add a failing test for the default report path and for explicit
`--output` preservation.

**Step 2:** Run the focused test and confirm the default-path assertion fails.

**Step 3:** Add a configuration-owned report-path resolver that returns
`Build/tools/CpgPersistenceBenchmark/reports/` when no path is supplied.

**Step 4:** Run the focused test and confirm it passes.

### Task 2: Give the runner a Build default

**Files:**
- Modify: `scripts/Run-CpgPersistenceBenchmark.ps1`

**Step 1:** Make `OutputRoot` optional and compute a timestamp-plus-guid run
directory beneath `Build/tools/CpgPersistenceBenchmark/runs/` only when it is
omitted.

**Step 2:** Preserve a caller-provided output root unchanged.

**Step 3:** Validate the script's parameter binding without launching a real
benchmark.

### Task 3: Route BenchmarkDotNet artifacts

**Files:**
- Modify: `tools/CpgMicrobenchmarks/Program.cs`

**Step 1:** Configure BenchmarkDotNet with an artifacts path under
`Build/tools/CpgMicrobenchmarks/BenchmarkDotNet/`.

**Step 2:** Build the microbenchmark project and confirm the code compiles.

### Task 4: Verify the full output contract

**Files:**
- Verify: `Directory.Build.props`
- Verify: both tool projects and performance tests

**Step 1:** Run the focused configuration tests.

**Step 2:** Query `BaseOutputPath` for both tool projects and confirm the
result is beneath `Build/tools/`.

**Step 3:** Build both tool projects with `--no-restore` and run
`git diff --check`.
