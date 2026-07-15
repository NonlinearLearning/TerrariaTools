# MarkAnalysisSnapshot Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reduce repeated Mark-stage graph, syntax, semantic-model, and region work without changing seed marks, graph bindings, or rewrite results.

**Architecture:** A `MarkAnalysisSnapshot` scoped to one `DeletionApplicationService.Analyze(...)` call owns an immutable graph-binding index and concurrent memoization for atomic candidates, operation lookups, target matches, and region facts. `RuleContext` and all derived structure-view contexts retain this snapshot; s-object Mark helpers reuse the resolved binding when constructing marks.

**Tech Stack:** C# / .NET 10, Roslyn `SemanticModel` and `IOperation`, xUnit.

---

### Task 1: Capture current public Mark behavior

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

**Step 1:** Add a nested s-object fixture containing identifier, member access, invocation, conditional access, object creation, and same-statement nesting.

**Step 2:** Add a failing test that compares `MarkingEngine.Run(...)` records between serial and group-parallel execution, including rule ID, span, reason, group key, order, and non-null primary graph node.

**Step 3:** Run the focused test and confirm it fails because the new fixture/assertion has not yet been added.

**Step 4:** Add only the test support required to compile the fixture and rerun until it proves the current behavior.

### Task 2: Add graph-binding index and carry resolved bindings

**Files:**
- Create: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleContext.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleHelpers/DeleteSObjectMarkRuleHelpers.cs`
- Modify: `src/RoslynPrototype/Marking/MarkingEngine.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

**Step 1:** Add a failing binding-priority regression with equal-span candidates.

**Step 2:** Run the focused test and confirm the snapshot API is unavailable.

**Step 3:** Build a run-scoped index using the existing priority ordering and graph enumeration tie-break; expose a single lookup that reports hit/miss telemetry.

**Step 4:** Use that lookup for both s-object graph eligibility and `MarkRecord.PrimaryGraphNode`; keep `MarkingEngine` fallback binding for all marks that arrive unbound.

**Step 5:** Rerun the focused tests and confirm the binding selection and output are unchanged.

### Task 3: Cache candidates, semantic facts, and regions

**Files:**
- Modify: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- Modify: `src/RoslynPrototype/Analysis/RuleSyntaxAnalysisHelpers.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleContext.cs`
- Modify: `src/RoslynPrototype/Analysis/otherAnalyzers/MarkRegionAnalyzer.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleHelpers/DeleteSObjectMarkRuleHelpers.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

**Step 1:** Add a failing telemetry test proving a non-target candidate does not materialize Mark region facts and that repeated candidate/ancestor evaluation reuses operation results.

**Step 2:** Run the test and confirm its required counters/API do not yet exist.

**Step 3:** Add root-reference-scoped atomic buckets, explicit cached-null operation lookup results, normalized target-set match keys, and cached region facts in the snapshot.

**Step 4:** Move region construction after target matching while preserving public anchor, span, and count semantics.

**Step 5:** Rerun focused tests and confirm output equivalence plus lower repeated-work counters.

### Task 4: Surface Mark telemetry and verify concurrent equivalence

**Files:**
- Modify: `src/RoslynPrototype/Marking/MarkingEngine.cs`
- Modify: `src/Application/DeletionApplicationService.cs`
- Modify: `src/RoslynPrototype/Rewrite/PrototypeAnalysisResult.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

**Step 1:** Add a failing test for telemetry that is observational only and reports candidates, cache hits/misses, operation queries, regions, graph binding lookups, and elapsed time per rule.

**Step 2:** Implement the smallest immutable result/telemetry plumbing needed to expose facts from the completed run.

**Step 3:** Run the serial/group-parallel focused tests until both output and telemetry invariants pass.

### Task 5: Full verification and task-state update

**Files:**
- Modify: `progress.md`
- Modify: `feature_list.json`

**Step 1:** Run the project build and targeted `PipelineComponentTests` / `StructureViewBuilderTests` suites.

**Step 2:** Run WorldGen, Main, and Player serial measurements plus the Terraria DOP 16 dry run when the source directory is available.

**Step 3:** Record commands, timings, telemetry deltas, known variance, and any unavailable external samples in the harness files.
