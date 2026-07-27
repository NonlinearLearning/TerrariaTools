# Rewrite Stage Test Coverage Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Establish unit, logic, compilation, and diagnostic performance coverage for rewriting without production changes.

**Architecture:** Contract tests own `PrototypeRewriter` behavior and replay equivalence. Host tests own directory plan replay and Roslyn compilation. Performance tests run fixed input samples, assert correctness first, then only report timing and allocation.

**Tech Stack:** .NET 10, xUnit, Roslyn CSharp APIs, `Stopwatch`.

**Theoretical coverage model:** The fixed fixture contains eight distinct expression-position shapes with 16 operations each: addition-left, addition-right, multiplication-left, relational condition, invocation argument, element-access index, cast expression, and parenthesized expression. This gives `8 × 16 = 128` operations. Each operation is checked through direct rewrite, in-memory plan replay, and JSON round-tripped persisted-plan replay: `128 × 3 = 384` path-operation combinations. The three execution paths are compared pairwise: `128 × C(3, 2) = 384` equivalence pairs. Plan integrity separately covers valid, stale-original-text, and overlapping-span plans (`3/3`), and the Host test covers one directory-plan replay plus Roslyn compilation (`1/1`). These are scenario counts, not source-line coverage.

---

### Task 1: Contract and logic coverage

**Files:** Modify `tests/Roslyn Prototype.ContractTests/Rewrite/DiffModelTests.cs`.

1. Write public-API tests for multi-edit direct/plan/persisted-plan equivalence and invalid text plans.
2. Run the focused ContractTests filter.
3. Change production only if the new test proves a public contract defect.

### Task 2: Directory compilation coverage

**Files:** Modify `tests/Roslyn Prototype.HostTests/Rewrite/RewritePlanPersistenceTests.cs`.

1. Capture and replay a directory plan.
2. Compile the replayed directory sources with Roslyn and assert no error diagnostics.
3. Run the focused HostTests filter.

### Task 3: Diagnostic rewrite performance coverage

**Files:** Create `tests/Roslyn Prototype.PerformanceTests/Rewrite/RewritePerformanceRegressionTests.cs`.

1. Use one fixed source fixture, warm once, then collect three plan-build and replay samples.
2. Assert equivalent source, edits, diff, and compilation before logging metrics.
3. Do not add machine-dependent timing or allocation thresholds.

### Task 4: Verification

Run the three focused project tests, their builds, harness consistency check, and `git diff --check` sequentially.
