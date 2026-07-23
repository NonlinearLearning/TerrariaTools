# Directory I/O And Log Performance Tests Implementation Plan

> **For Codex:** Use test-driven development task-by-task.

**Goal:** Add deterministic multi-file directory performance regressions for asynchronous
source loading and text-log output.

**Architecture:** Extend the existing performance regression test class with a generated
directory fixture and a measurement helper. The helper calls the public command host and
returns observable result and log metadata; assertions compare result equality and record
completeness while timing remains diagnostic output.

**Tech Stack:** .NET 10, xUnit, RoslynPrototype command host.

---

### Task 1: Add the failing multi-file directory measurement test

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/Performance/PerformanceOptimizationRegressionTests.cs`

1. Add a test that measures DOP 1 and DOP 16 runs with and without text logs.
2. Assert the intended fixture size, equivalent analysis results, runtime completion, and
   one analysis `file completed` event per source file.
3. Run the focused test and confirm it fails because the measurement helper is absent.

### Task 2: Implement the minimal test-only fixture and measurement helper

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/Performance/PerformanceOptimizationRegressionTests.cs`

1. Create the fixed 33-file source directory under the test temporary directory.
2. Add a helper that invokes `DeletionCommandHost.AnalyzeFromArgsAsync`, times the call,
   reads log lines, and returns immutable measurement data.
3. Re-run the focused test and verify the expected assertions pass.

### Task 3: Verify the affected test surface

**Files:**
- Verify: `tests/RoslynDeletionPrototype.Tests/Performance/PerformanceOptimizationRegressionTests.cs`
- Verify: `tests/RoslynDeletionPrototype.Tests/Logging/TextLogSystemTests.cs`

1. Build the test project without restore.
2. Run the performance and text-log focused test groups.
3. Run `git diff --check` and the harness consistency check for the added documents.
