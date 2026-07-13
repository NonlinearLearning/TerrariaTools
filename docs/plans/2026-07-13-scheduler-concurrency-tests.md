# Scheduler Concurrency Tests Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move scheduler async/concurrency tests into a dedicated tests folder and extend them with large real-code-set stress cases.

**Architecture:** Keep production scheduler logic in `src/RoslynPrototype/RuleServices/ExecutionRuntime.cs`. Put scheduler-specific tests under `tests/RoslynDeletionPrototype.Tests/Concurrency/` so pipeline behavior tests stay focused.

**Tech Stack:** C# 10 preview target via net10.0, xUnit, `TaskCompletionSource`, `CancellationTokenSource`, async `FileStream`.

---

### Task 1: Extract Existing Tests

**Files:**
- Create: `tests/RoslynDeletionPrototype.Tests/Concurrency/BoundedRuleStageSchedulerConcurrencyTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

**Steps:**
1. Move scheduler peak, async order, cancellation, and Terraria code-set tests into the new file.
2. Move private scheduler test helpers into the new test class.
3. Remove `ITestOutputHelper` dependency from `PipelineComponentTests`.

### Task 2: Add Complex Mixed Behavior Tests

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/Concurrency/BoundedRuleStageSchedulerConcurrencyTests.cs`

**Steps:**
1. Add mixed fast/slow/async-IO test over the Terraria code set.
2. Add exception propagation test over the Terraria code set.
3. Keep all tests bounded with timeouts and explicit console output.

### Task 3: Verify

**Commands:**
- `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter BoundedRuleStageSchedulerConcurrencyTests --logger "console;verbosity=detailed"`
- `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter PipelineComponentTests`
- `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false`
- `dotnet build .\src\Host\Host.csproj --no-restore -p:UseSharedCompilation=false`
