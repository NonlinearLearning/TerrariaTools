# Dome Independent Tool Architecture Design

## Summary

This document captures the v1 architecture for `dome` as an independent tool. The design moves `dome` away from the legacy host-project model and establishes a fixed execution flow:

`Analysis -> Mark -> Plan -> Rewrite -> Report`

Key decisions:

- `dome` is an independent tool, not a feature branch hidden inside `TerrariaTools.csproj`.
- `Plan` is an independent and auditable artifact.
- `Plan` is the single source of truth for execution.
- `Rewrite` executes the plan and does not re-run rule logic.
- The application uses a lightweight staged pipeline.
- There is no checkpoint resume, no in-place rewrite, and failure is fail-fast.
- The v1 scope is limited to regular member bodies and does not add first-class support for lambda or local functions.

## Current State and Problems

The current `dome` codebase has useful analysis and rewrite experiments, but it still behaves like a sidecar research area instead of an independent productized tool.

Main issues:

- `dome` is excluded from the main project compilation and still references the legacy project.
- `Program.cs` is demo-menu oriented rather than application-entry oriented.
- `RuleEngine` currently mixes analysis setup, propagation, structural cascading, decision-making, and rewrite annotations in one place.
- Rule registration is reflection-heavy and implicit.
- Test code lives under `Build/UnitTests_dome`, which is closer to a temporary test layout than a stable project structure.
- Roslyn objects are exposed too broadly in existing abstractions, which makes long-term layering brittle.

## Goals

- Make `dome` a standalone tool with a stable application entry point.
- Separate runtime Roslyn concerns from stable cross-layer contracts.
- Turn rule outcomes into an explicit plan that can be reviewed, audited, and executed.
- Keep the first version narrow enough to implement safely.
- Preserve the current Roslyn-based implementation advantage without letting Roslyn types become the public architecture.

## Non-Goals

- No checkpoint resume.
- No cross-run plan replay guarantees.
- No in-place source rewriting.
- No dynamic plugin loading in v1.
- No first-class support for lambda or local function targets in the initial plan model.

## Architectural Layers

### Dome.Cli

Responsibilities:

- Parse commands and arguments.
- Build `RunRequest`.
- Invoke the application pipeline.
- Convert result state into exit codes.

It should not:

- Contain rewrite logic.
- Reach into Roslyn directly.
- Infer missing plan semantics.

### Dome.Application

Responsibilities:

- Orchestrate the staged pipeline.
- Control stage order and fail-fast behavior.
- Hold short-lived runtime execution context for one run.
- Coordinate output directory creation and final report writing.

Pipeline stages:

1. `LoadWorkspace`
2. `BuildAnalysisView`
3. `ExecuteMarking`
4. `CompileAuditPlan`
5. `ExecuteRewriteFromPlan`
6. `WriteReport`

Design constraints:

- Stages 1 through 4 are read-only with respect to the source tree.
- `Rewrite` happens once, at the end.
- No stage is allowed to silently "help" a later stage by mutating the syntax tree in place.

### Dome.Core

Responsibilities:

- Define stable run contracts.
- Define plan and report models.
- Define failure codes and result states.

It should contain:

- `RunRequest`
- `RunResult`
- `FailureCode`
- `AnalysisView`
- `AuditPlan`
- `PlanTarget`
- `PlanAction`
- `PlanReason`
- `PlanConflict`

It should not contain:

- `SyntaxNode`
- `SemanticModel`
- `Compilation`
- `Solution`
- `ISymbol`
- CLI parsing code
- MSBuild loading code

### Dome.Analysis.Roslyn

Responsibilities:

- Load Roslyn workspace data.
- Build the analysis view required by rules.
- Project Roslyn facts into stable models.

It is allowed to use:

- `Solution`
- `Compilation`
- `SyntaxTree`
- `SyntaxNode`
- `SemanticModel`
- `ISymbol`

These are implementation details and must not become cross-layer public contracts.

### Dome.Rules

Responsibilities:

- Consume the analysis view.
- Determine initial seeds.
- Execute propagation and structural cascading.
- Produce rule outcomes and reasons.

Design constraints:

- Rules are registered explicitly.
- Reflection-based discovery can remain only as a temporary migration aid.
- High-risk safety rules such as inheritance and interface implementation protection must execute with fixed priority.

### Dome.Plan

Responsibilities:

- Compile marking outcomes into an explicit, auditable, executable plan.
- Resolve conflicts before rewrite.
- Produce execution order.

This is the architectural center of v1.

### Dome.Rewrite.Roslyn

Responsibilities:

- Consume the plan.
- Locate target nodes against the original source state.
- Execute only the declared actions.

Design constraints:

- No fallback rule inference.
- No hidden propagation.
- No direct reads of analysis graphs for decision making.
- If the plan is incomplete or conflicting, rewrite fails.

### Dome.Reporting

Responsibilities:

- Write audit plan output.
- Write execution summary and failures.
- Emit human-readable artifacts for review.

## Execution Model

The system is intentionally designed to avoid mid-pipeline syntax tree mutation.

Why:

- It keeps target stability manageable.
- It avoids repeated target re-indexing after each step.
- It allows the plan to be compiled from one consistent original source state.

Execution model:

1. Analyze the original source.
2. Mark targets and collect reasoning.
3. Compile a standalone plan.
4. Execute a single rewrite pass from that plan.
5. Emit output artifacts and report.

## Plan as Executable Source of Truth

The plan is not a side-report. It is the execution truth for rewrite.

This means:

- `Rewrite` may not infer new actions.
- `Rewrite` may not resolve rule conflicts on its own.
- `Rewrite` may not consult propagation rules to "fill in" missing details.

The plan must therefore capture:

- what target is changed
- what action is executed
- why it was chosen
- what produced it
- whether it cascaded from another target
- in what order it should run
- whether any conflict existed and how it was resolved

## Plan Model

### PlanMetadata

Minimum fields:

- tool name
- plan version
- generated time
- input path
- output path
- rule set identifier
- run mode

### PlanTarget

The target locator for v1 is:

- `DocumentPath`
- `MemberId`
- `MemberKind`
- `TargetKind`
- `SpanStart`
- `SpanLength`
- `DisplayText`

This is intentionally the first stable boundary for execution.

### MemberId

The primary member identifier is the metadata signature form, for example:

- `Namespace.Type.Method(paramTypes)`
- `Namespace.Type..ctor(paramTypes)`
- `Namespace.Type.Property`

The metadata-style ID is the execution identity.
An additional human-readable signature may be stored for reporting.

### Why not use SyntaxNode directly in Core

`SyntaxNode` can still exist in Roslyn runtime implementations, but not as a stable `Core` contract.

Reasons:

- it is bound to Roslyn
- it is not suited for independent audit artifacts
- it is not a reliable long-term plan identity
- it encourages cross-layer leakage

### Why not use a heavier locator in v1

A heavier locator such as `member + statement ordinal + fingerprint` is more robust but would significantly increase model complexity.

Given the v1 constraints:

- no checkpoint resume
- no cross-run replay guarantee
- no mid-pipeline syntax mutation

`DocumentPath + MemberId + TextSpan` is the best balance.

## Plan Actions

The initial action set is intentionally small:

- `Delete`
- `CommentOut`
- `ReplaceWithDefault`
- `AddReturn`

Each action must include enough payload to execute without rule re-evaluation.

## Plan Reasons

Each planned action must carry reason data.

Minimum reason categories:

- direct rule hit
- propagated from another target
- structural cascading
- safety/risk note

Recommended fields:

- `RuleId`
- `ReasonText`
- `SourceTargetId` or equivalent source reference
- `Severity`

## Conflicts

Conflicts are resolved in the planning phase, not in rewrite.

Examples:

- same target receives both `Delete` and `ReplaceWithDefault`
- one action requires removal while another requires preservation for safety
- structural cascade collides with a high-priority safety rule

Policy:

- planning either resolves the conflict deterministically or records it as blocking
- rewrite must fail on unresolved conflicts

## V1 Target Coverage

The initial version covers regular member-body targets only:

- method bodies
- constructors
- property accessors
- field or property initializers where explicitly supported by rule design
- regular control-flow statements inside those members

The initial version does not give first-class target model support to:

- lambda expressions
- local functions
- mixed nested function scopes

These can remain unsupported or explicitly rejected in v1.

## Rule System Constraints

- Rule registration should move toward explicit module registration.
- Inheritance and interface-implementation safety must be fixed-priority guards.
- Structural behaviors such as `try-catch`, loop cascading, and object initializer reset should be represented explicitly during planning.
- The rule engine should no longer own rewrite semantics directly.

## Output Model

The only supported output mode in v1 is writing to a dedicated result directory.

Artifacts should include at minimum:

- rewritten source output
- audit plan document
- run summary
- failure report when applicable

There is no in-place rewrite mode in v1.

## Failure Model

Failure strategy is fail-fast.

Representative failure codes:

- `WorkspaceLoadFailed`
- `AnalysisFailed`
- `PlanCompileFailed`
- `RewriteFailed`
- `ReportFailed`

Rules:

- a stage failure stops the run
- partial success should not be reported as success
- output should remain explicit about whether the run completed

## Testing Strategy

### Analysis Tests

- `MemberId` generation for overloads, constructors, and property accessors.
- `AnalysisView` projection correctness for dependencies, references, and inheritance facts.

### Rule and Plan Tests

- rule hits produce auditable reasons
- propagation chains are preserved into the plan
- cascading behavior is represented explicitly
- conflict resolution is deterministic
- safety rules block invalid actions on virtual, override, or interface-related members

### Rewrite Tests

- rewrite executes only planned actions
- missing or mismatched targets fail clearly
- unresolved plan conflicts fail clearly
- `DocumentPath + MemberId + TextSpan` can resolve regular member-body targets reliably in supported cases

### End-to-End Tests

- full `Analysis -> Mark -> Plan -> Rewrite -> Report` path
- result directory contains rewritten sources, plan, and report
- failed runs do not masquerade as complete runs

## Migration Direction

The old experimental structure can remain temporarily as source material, but the new architecture should move toward:

- a proper CLI entry point
- a formal test project
- explicit module ownership
- plan-driven rewrite execution

The legacy `RuleEngine` should be split logically into:

- analysis fact consumption
- propagation and marking
- plan compilation
- rewrite execution

## Recommended File/Project Direction

Suggested structure:

- `dome/src/Dome.Cli`
- `dome/src/Dome.Application`
- `dome/src/Dome.Core`
- `dome/src/Dome.Analysis.Roslyn`
- `dome/src/Dome.Rules`
- `dome/src/Dome.Plan`
- `dome/src/Dome.Rewrite.Roslyn`
- `dome/src/Dome.Reporting`
- `dome/tests/Dome.Tests`

This can be achieved incrementally if a full immediate split is too expensive.

## Final Position

The v1 design deliberately chooses auditability and clean execution boundaries over maximum short-term implementation convenience.

The most important architectural rule is:

**Rewrite executes the plan. It does not reinterpret the rules.**

That single rule is what turns `dome` from a rewrite experiment into a proper independent tool.
