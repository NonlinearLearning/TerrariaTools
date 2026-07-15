# Runtime Metrics Log Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use test-driven-development to implement this plan task-by-task.

**Goal:** Add opt-in five-second runtime metric logging for each deletion analysis.

**Architecture:** `DeletionCommandHost` will own a disposable sampler around either single-file or directory analysis. The sampler writes JSON Lines records to a caller-selected path every five seconds and writes one final completion or fault record. It samples process, GC, and ThreadPool counters without changing analysis scheduling.

**Tech Stack:** .NET 10, xUnit, `System.Diagnostics.Process`, `System.GC`, `System.Threading.ThreadPool`, `System.Text.Json`.

---

### Task 1: Lock down the CLI contract

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Modify: `src/Host/DeletionApplicationOptions.cs`

1. Add a test that passes `--runtime-metrics-log <path>` to an analysis and asserts a JSONL completion record contains DOP, elapsed time, GC counts, managed allocation, working set, and ThreadPool fields.
2. Run the focused test and confirm it fails because the option has no implementation.
3. Add strict option-path parsing, including rejection of a missing path.
4. Re-run the focused test.

### Task 2: Add periodic sampling

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Modify: `src/Host/DeletionCommandHost.cs`
- Create: `src/Host/RuntimeMetricsLog.cs`

1. Add a test using a blocking analysis seam or sufficiently long fixture to require at least one periodic record before completion.
2. Run it and confirm it fails.
3. Implement a disposable sampler that starts immediately, writes every five seconds, serializes writes under a lock, and writes a final status record in `Dispose` or fault handling.
4. Wrap both directory and single-file analysis paths in the sampler.
5. Re-run the focused tests.

### Task 3: Validate user workflow

**Files:**
- Modify: `docs/quick-start.md`
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. Add a DOP `8`, `12`, `14`, `16` serial-run regression that writes all records to a new log file and asserts four completed runs in that exact order.
2. Document the option and the serial command sequence.
3. Run focused tests, the related concurrency suite, project build, and harness consistency check.

