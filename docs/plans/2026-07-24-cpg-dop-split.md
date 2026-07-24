# CPG And Directory DOP Split Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Separate directory scheduling DOP from per-file CPG partition DOP so the DOP 12 regression can be measured without changing legacy CLI behavior.

**Architecture:** `RoslynPrototypeExecutionOptions` receives an optional CPG DOP override. The runtime retains the global DOP for directory, helper, and group scheduling, while `DeletionApplicationService` gives the CPG builder the override when present. A missing override inherits the global effective DOP.

**Tech Stack:** .NET 10, Roslyn, xUnit, existing text runtime and analysis logs.

---

### Task 1: Define And Validate The CPG Override

**Files:**
- Modify: `src/RoslynPrototype/RuleServices/ExecutionRuntime.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Write failing tests**

Add focused tests for `DeletionAnalysisRuntime.CreateFromOptions`:

```csharp
Assert.Equal(12, runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
Assert.Equal(1, runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism);
```

Use options containing `max-degree-of-parallelism=12` and `cpg-max-degree-of-parallelism=1`. Add a second test with no CPG option and assert that the effective CPG DOP inherits 12. Add `[Theory]` cases for `0`, `-1`, `invalid`, and a valueless `true` option; each must throw `ArgumentException` naming `--cpg-max-degree-of-parallelism`.

**Step 2: Run the focused test and verify RED**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --filter "FullyQualifiedName~PipelineComponentTests" -p:UseSharedCompilation=false
```

Expected: the new tests fail because the execution options have no CPG-specific value.

**Step 3: Implement the smallest option model**

Add an optional `CpgMaxDegreeOfParallelism` field to `RoslynPrototypeExecutionOptions` and an `EffectiveCpgMaxDegreeOfParallelism` property that falls back to `EffectiveMaxDegreeOfParallelism`. In `DeletionAnalysisRuntime.CreateExecutionOptions`, parse only `cpg-max-degree-of-parallelism`; when present require a positive integer and throw an `ArgumentException` otherwise. Leave `ResolveMaxDegreeOfParallelism` unchanged so legacy global-option semantics do not drift.

**Step 4: Run the focused test and verify GREEN**

Run the command from step 2. Expected: all selected tests pass.

**Step 5: Commit the isolated option contract**

Stage only the two files. Use a Lore-format commit that records the legacy fallback and invalid-value rejection behavior.

### Task 2: Route The Override Only To The CPG Builder

**Files:**
- Modify: `src/Application/DeletionApplicationService.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Write the failing builder-routing test**

Create an analysis with runtime global DOP 12 and CPG DOP 1. Assert:

```csharp
Assert.Equal(1, result.CpgBuildTelemetry!.MaxDegreeOfParallelism);
Assert.Equal(12, runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
```

Retain the existing no-override test as the regression for legacy inheritance.

**Step 2: Run the focused test and verify RED**

Run the command from Task 1, Step 2. Expected: the explicit CPG assertion fails because the builder still receives the global DOP.

**Step 3: Implement the one-line routing change**

In `DeletionApplicationService.BuildAnalysisContext`, replace the builder option assignment with `runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism`. Do not alter directory scheduling, helper scheduling, group scheduling, CPG pass algorithms, or defaults.

**Step 4: Run focused equivalence tests and verify GREEN**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --filter "FullyQualifiedName~PipelineComponentTests" -p:UseSharedCompilation=false
```

Expected: builder routing and existing directory-DOP equivalence tests pass.

**Step 5: Commit the routing change**

Stage only the application and test files. Use a Lore-format commit stating that the option changes diagnostics only and leaves default behavior intact.

### Task 3: Expose The CLI Contract And Lock Directory Equivalence

**Files:**
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- Modify: `docs/cli-reference.md`
- Modify: `docs/quick-start.md`
- Modify: `docs/developer-guide.md`

**Step 1: Write failing CLI-flow tests**

Extend the existing directory delete-class DOP test with `(directory=12, cpg=1)` and `(directory=1, cpg=12)`. Assert both results are equivalent to the serial result. Add a CLI argument test proving an invalid CPG override throws the documented `ArgumentException`.

**Step 2: Run the focused test and verify RED**

Run the Host test command. Expected: the result test fails until the CPG override is wired through the CLI-created runtime.

**Step 3: Document the option**

Document that `--max-degree-of-parallelism` continues to limit directory/rule work, while `--cpg-max-degree-of-parallelism` limits per-file CPG partitioning and inherits the global value by default. Include the three diagnosis commands; label them measurement-only and state that they do not change defaults.

**Step 4: Run contract verification**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build --filter "FullyQualifiedName~PipelineComponentTests" -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

Expected: build, focused tests, consistency check, and whitespace check pass.

**Step 5: Commit CLI documentation and contract coverage**

Stage only Task 3 files. Use a Lore-format commit with the documented measurement boundary and remaining real-source benchmark gap.

### Task 4: Run The Controlled Real-Source Matrix

**Files:**
- Create: `Build/cpg-dop-split-YYYYMMDD/` benchmark logs and a concise result table outside source control

**Step 1: Establish the fixed input and process settings**

Use the same directory, target rule, `--skip-rewrite`, `--no-diff`, `--runtime-log`, `--analysis-log`, and `--log-profile benchmark` for every run. Record input path/hash, commit, SDK, processor count, and command line.

**Step 2: Run one warmup per case**

Run the three cases below sequentially, with a bounded external process and separate logs:

```text
directory=12, cpg=1
directory=1, cpg=12
directory=12, cpg=12
```

**Step 3: Run three measured samples per case**

Persist each run's runtime and analysis logs. Do not run cases concurrently. A timeout, resource exhaustion, or missing completed event is an invalid sample, not a benchmark result.

**Step 4: Compare only comparable completed runs**

Report median wall-clock, completed-file count, CPG operation/syntax/data-flow/freeze aggregates, elapsed/allocated/GC/heap/working-set/ThreadPool values. Separate accumulated per-file elapsed from wall-clock in the table.

**Step 5: Preserve the default**

Do not alter default DOP from this measurement alone. Require graph, rule, and rewrite equivalence plus stable repeated measurements before any default proposal.

## Final Verification

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

Completion requires the option-flow tests, the directory equivalence tests, fresh verification output, and three valid completed samples for each real-source matrix case. The feature remains `in_progress` until the broader feature definition of done is met.
