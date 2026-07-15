# Mark Analysis Rule Ledger Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Expose thread-safe, per-Mark-rule telemetry without changing mark results or graph binding behavior.

**Architecture:** `MarkingEngine` opens a rule-scoped telemetry scope around each `RuleDefinitionMark`. `MarkAnalysisSnapshot` attributes cache activity to that scope through async-flow-local state and returns immutable per-rule rows in the existing run-scoped telemetry payload.

**Tech Stack:** C#, .NET 10, xUnit, Roslyn.

---

### Task 1: Lock the public telemetry contract

**Files:**

- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Modify: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`

**Step 1:** Add a failing test asserting one ledger row per invoked rule, raw candidate and accepted-mark counts, and graph-binding fallback count.

**Step 2:** Run the focused test and confirm it fails because the per-rule ledger does not exist.

**Step 3:** Add immutable ledger row types to `MarkAnalysisTelemetry` with stable ordering by rule registration order.

**Step 4:** Re-run the focused test and confirm it passes.

### Task 2: Attribute Mark work to its rule

**Files:**

- Modify: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleContext.cs`
- Modify: `src/RoslynPrototype/Marking/MarkingEngine.cs`

**Step 1:** Add a thread-safe rule telemetry scope that records candidate, accepted, binding fallback, and cache counters.

**Step 2:** Open the scope only around `RuleDefinitionMark.Mark`; preserve serial and group-parallel mark output.

**Step 3:** Make snapshot cache probes record both the existing run-wide counters and the active rule counter.

**Step 4:** Run serial/parallel Mark regression tests and confirm equal mark keys plus stable ledger rows.

### Task 3: Verify and hand off

**Files:**

- Modify: `progress.md`
- Modify: `feature_list.json`

**Step 1:** Build `RoslynPrototype` and run the focused test group.

**Step 2:** Run the relevant complete test project if it completes within the command limit; otherwise record its exact verified scope.

**Step 3:** Update handoff state with the contract, evidence, and remaining real-Terraria measurement gap.
