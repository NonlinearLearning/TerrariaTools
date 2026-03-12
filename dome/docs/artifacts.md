# Dome v1 Artifact Contract

## `analysis.json`

Written only in `analyze` mode.

Primary purpose:

- expose projected targets
- expose use/def facts
- expose analysis edges

Key data includes:

- `Targets`
- `Edges`
- `Target.DocumentPath`
- `Target.MemberId`
- `DefinesSymbols`
- `UsesSymbols`
- `IsHighRisk`

## `audit-plan.json`

Written in `plan` and `run` modes.

This is the executable plan contract. Key sections:

- `Metadata`
- `Changes`
- `Conflicts`

Each `Change` contains:

- `ExecutionOrder`
- `Target`
- `Action`
- `Reason`
- `Chain`

`Chain` is always present in the JSON shape:

- `null` for direct hits
- a `PropagationChain` object for propagated changes

Compatibility fields remain in `Reason`:

- `SourceTargetKey`
- `SourceTargetDisplayText`
- `RelatedSymbolKeys`
- `RelatedSymbolNames`

These summarize the last hop and stay aligned with `Chain.Hops`.

## `report.json`

Written in all modes, including failure cases.

Primary purpose:

- summarize run outcome
- explain failure semantics
- expose artifact inventory
- expose risk and conflict summaries

Key fields:

- `IsSuccess`
- `FailureCode`
- `AnalysisTargets`
- `PlannedChanges`
- `Conflicts`
- `RewrittenDocuments`
- `GeneratedArtifacts`
- `FailureSummary`
- `ConflictSummaries`
- `RiskSummary`
- `Message`

Failure expectations:

- `PlanCompileFailed`
  `report.json` exists and explains conflicts
- `RewriteFailed`
  `report.json` exists and explains the rewrite failure
- `AnalyzeOnly`
  no `audit-plan.json`, no `rewritten/**`

## `rewritten/**`

Written only in `run` mode and only on successful rewrite.

Properties:

- relative layout mirrors the input tree
- each output file is explainable by `audit-plan.json`
- rewrite is statement-oriented in v1

Examples:

- `rewritten/Root.cs`
- `rewritten/Features/Nested.cs`

## Conflict Semantics

Plan conflicts are surfaced in both `audit-plan.json` and `report.json`.

Stable fields:

- `ConflictCode`
- `Reason`
- conflicting action kinds
- target identity

v1 does not attempt conflict auto-resolution.
