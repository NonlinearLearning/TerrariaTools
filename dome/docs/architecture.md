# Dome v1 Architecture

## Execution Model

`dome` uses a fixed pipeline:

`Analysis -> Mark -> Plan -> Rewrite -> Report`

The pipeline is single-run and plan-driven:

- `Analysis` projects Roslyn facts into `AnalysisView`
- `Mark` applies `Seed -> Propagate -> Protect`
- `Plan` compiles decisions into `AuditPlan`
- `Rewrite` executes only the compiled plan
- `Report` writes stable machine-readable artifacts

There is no checkpoint resume, no mid-run rewrite feedback loop, and no rewrite-time rule inference.

## Project Layout

- `src/Core`
  Stable contracts and shared execution models
- `src/Analysis/Roslyn`
  Roslyn-based syntax and semantic projection
- `src/Rules`
  Seed, propagation, and protection logic
- `src/Plan`
  Compiles mark decisions into the executable audit plan
- `src/Rewrite/Roslyn`
  Executes plan actions against syntax trees
- `src/Reporting`
  Writes JSON artifacts
- `src/Application`
  Orchestrates pipeline stages and output modes
- `src/Cli`
  Command parsing, config loading, exit codes
- `tests/Dome.Tests`
  Analysis, Rules, Plan, Rewrite, Cli, and Application tests

## Stable v1 Contracts

The v1 public contract is centered on:

- `RunRequest`
- `RunResult`
- `FailureCode`
- `AnalysisView`
- `AuditPlan`
- `PlanConflict`
- `RunReport`

Important stable fields exposed to external consumers:

- `ConflictCode`
- `FailureSummary`
- `ConflictSummaries`
- `RiskSummary`
- `GeneratedArtifacts`

## Plan-driven Rewrite

`AuditPlan` is the execution source of truth.

`Rewrite` does not:

- infer additional propagation
- inspect rule logic to decide actions
- resolve unresolved conflicts
- silently recover from target drift

It resolves targets using:

1. `DocumentPath + MemberId`
2. `SpanStart + SpanLength`
3. `DisplayText`

If resolution fails, rewrite returns `RewriteFailed`.

## Supported Analysis Scope

v1 analysis covers regular member bodies and simple initializer scenarios:

- method bodies
- constructor bodies
- property accessors
- field initializers
- property initializers

Dataflow support is intentionally minimal:

- local declarations
- simple assignments
- identifier reads
- simple return statements

No full CFG is used in v1.

## Protection Model

Protection happens before executable plan output.

High-risk targets are analyzed but blocked from executable mark decisions when they belong to:

- `virtual` members
- `override` members
- `abstract` members
- interface implementations
- other currently unsafe rewrite paths

Protected targets are summarized in `report.json` through `RiskSummary`.
