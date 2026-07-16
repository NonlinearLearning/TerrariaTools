# Text Log System Execution Design

## Goal

Turn the text log redesign into one executable document that can be implemented, verified, and closed without reopening architecture every round.

This document does four things at once:

1. records the reference projects and the exact direction borrowed from each
2. freezes the hard boundaries of this repo's log system
3. defines the minimum implementation slices that produce a usable closed loop
4. defines stop conditions, verification, and rollback points for each slice

This document is additive. It does not replace:

- `docs/plans/2026-07-16-text-log-system-design.md`
- `docs/plans/2026-07-16-text-log-subdesign-event-taxonomy-and-fields.md`
- `docs/plans/2026-07-16-text-log-subdesign-filtering-and-views.md`
- `docs/plans/2026-07-16-text-log-subdesign-sink-and-lifecycle.md`
- `docs/plans/2026-07-16-text-log-subdesign-migration-and-compatibility.md`

It is the execution-facing parent document.

## Why A Second Document Exists

The existing design set already captures the direction, but it is still split by topic.

That is useful for design review and bad for execution because the implementer still has to answer:

- which file to change first
- which old writer to remove first
- which compatibility surface must stay alive
- what counts as "done enough" to stop
- what must never enter the log pipeline

This document answers those questions in one place.

## Reference Projects

The reference direction was taken from high-star upstream projects reviewed on 2026-07-16.

### 1. Serilog

Repository:

- <https://github.com/serilog/serilog>

Borrowed direction:

- event pipeline separated from sinks
- logging producers should emit a stable event object, not build ad-hoc strings in business code
- sink routing is a pipeline concern, not an application-service concern

What we keep:

- the idea of a stable event model
- the idea of per-sink filtering
- the separation between event production and output writing

What we reject:

- JSON-first assumptions
- dependency-heavy adoption
- broad templating surface on day one

### 2. zap

Repository:

- <https://github.com/uber-go/zap>

Borrowed direction:

- small core contract
- `Check`-then-`Write` thinking on hot paths
- explicit flush lifecycle
- structured context first, presentation later

What we keep:

- cheap filtering before expensive field construction
- explicit flush and failure ownership
- narrow core interfaces

What we reject:

- Go-specific API style
- implicit acceptance of a richer runtime logger API than this repo needs

### 3. zerolog

Repository:

- <https://github.com/rs/zerolog>

Borrowed direction:

- writer composition
- low-allocation hot path discipline
- event builder reuse in high-frequency logging

What we keep:

- pooled builder or pooled field buffer idea
- one event built once, then written once
- keeping file/phase logging cheap enough for directory runs

What we reject:

- JSON/JSONL payload shape
- public fluent API complexity

### 4. NLog

Repository:

- <https://github.com/NLog/NLog>

Borrowed direction:

- layout/render plan is a first-class concept
- rendering should be compiled from stable field rules, not handwritten inline in many services

What we keep:

- precompiled render plans for `compact`, `normal`, `diagnostic`, and `benchmark`
- formatter owns field order and quoting

What we reject:

- exposing a general template DSL in the first rollout

## Repo-Specific Design Direction

The result is not "copy Serilog" or "copy zap."

The result is a repo-specific hybrid:

1. Serilog-style event pipeline separation
2. zap-style small core and explicit flush
3. zerolog-style hot-path discipline
4. NLog-style render-plan formatting
5. repo-specific text `.log` format with fixed `key=value` records

That hybrid is appropriate here because this repo has three non-negotiable constraints:

1. no JSONL content
2. no diff inside logs
3. no spread of custom writer logic across business services

## Hard Boundaries

These boundaries are architectural, not "best effort."

### Boundary 1: no JSON or JSONL content

Forbidden:

- JSON objects per line
- nested payload blocks
- anonymous object serialization as the log pipeline

Required:

- one-line text `.log`
- stable `key=value` fields
- deterministic field order

### Boundary 2: no diff in logs

Forbidden:

- full diff text
- diff hunk text
- original text
- replacement text
- `DiffText`
- `diffSummary=*`
- `diffFiles=*`
- `diffBlocks=*`
- any diff-specific category or event

Allowed:

- general execution facts such as `edits=12`

This keeps observability separate from rewrite artifact transport.

### Boundary 3: no embedded writer classes inside business orchestration

Forbidden target state:

- `DeletionDirectoryAnalysisService` owning custom output schemas and file writers
- `RuntimeMetricsLog` remaining a JSONL serializer plus file owner plus timer plus formatter

Required target state:

- business services emit through shared logging abstractions
- sink and formatter own output mechanics

### Boundary 4: no giant completion event

Forbidden target state:

- one terminal record carrying run, CPG, mark, memory, cache, and diagnostic details all mixed together

Required target state:

- `run.completed` stays narrow
- CPG detail stays in `cpg.summary`
- mark detail stays in `mark.summary`
- phase detail stays in `phase.completed`
- memory detail stays in `memory.snapshot`

## The Closed-Loop Implementation Target

The first usable closed loop is smaller than the full ideal system.

This document defines "closed loop" as:

1. one run can emit `.log` text records end to end
2. old JSONL emitters are no longer the primary path
3. users can filter by at least level and category
4. tests can parse and assert the new text events
5. no diff text or diff summary enters the log path

That means the first closure does not require:

- a public template DSL
- a plugin sink system
- a brand-new console formatter
- every legacy option to be removed immediately

## Target Output Shape

The canonical line shape remains:

```text
ts=<utc> lvl=<level> cat=<category> evt=<event> msg="<fixed message>" run=<runId> ...
```

Example:

```text
ts=2026-07-16T12:52:10.000Z lvl=INFO cat=run evt=completed msg="analysis completed" run=01K0M op=delete-class inputKind=directory files=1503/1503 elapsedMs=1033075 edits=0 diags=0 status=completed
```

This shape is chosen because it closes three goals at once:

1. grep-friendly
2. machine-stable
3. still readable without a dedicated viewer

## Minimal Architecture To Build

Create a shared surface under `src/Host/Logging/`.

### Required types

- `TextLogEvent`
- `TextLogLevel`
- `TextLogCategory`
- `TextLogFilter`
- `ITextLogSink`
- `TextLogFormatter`
- `TextLogFileSink`
- `RunTextLogWriter`
- `AnalysisTextLogWriter`
- `RunLogContext`

### Responsibility split

`TextLogEvent`

- immutable representation of one logical record

`TextLogFilter`

- level enablement
- category enablement
- later event-type enablement

`TextLogFormatter`

- field ordering
- quoting
- escaping
- omission of absent fields
- render-plan selection by view

`TextLogFileSink`

- queue ownership
- background writer
- file handle ownership
- flush
- failure propagation

`RunTextLogWriter`

- `run.started`
- `run.sampled`
- `run.completed`
- `run.failed`
- optional summary events

`AnalysisTextLogWriter`

- `file.completed`
- `phase.completed`
- `memory.snapshot`
- `io.writer.failed`

## Execution Order

This is the order that minimizes risk and preserves a clean rollback path.

## Phase 0: lock the contract in tests

Goal:

- stop the design from drifting while implementation starts

Tasks:

1. add a text log line parser helper for tests
2. add formatter tests for quoting, ordering, and omission rules
3. add negative tests that assert diff fields are rejected or absent

Stop condition:

- parser helper exists
- formatter contract is testable without touching directory analysis yet

Why first:

- without this, every later code change debates string shape again

## Phase 1: introduce the shared logging core

Goal:

- create the new abstractions without moving the whole host at once

Tasks:

1. add `src/Host/Logging/`
2. implement `TextLogEvent`
3. implement `TextLogFilter`
4. implement `TextLogFormatter`
5. implement `TextLogFileSink`

Required result:

- a small test can emit one event into a `.log` file and assert the exact line

Stop condition:

- one sink writes deterministic text records
- explicit flush works
- file write failure can be surfaced

Rollback point:

- if this phase is wrong, it can be reverted without touching runtime orchestration yet

## Phase 2: replace runtime metrics logging first

Goal:

- move the lowest-cardinality emitter first

Why runtime first:

- fewer events
- easier to inspect manually
- lower blast radius than per-file logging

Tasks:

1. replace `RuntimeMetricsLog` JSONL writing with `RunTextLogWriter`
2. keep the existing option name for now
3. emit:
   - `run.started`
   - `run.sampled`
   - `run.completed`
   - `run.failed`
4. add optional `cpg.summary` and `mark.summary` behind filters

Required result:

- one runtime `.log` file works end to end
- no JSON object lines remain in the runtime path

Stop condition:

- runtime log is stable
- `run.completed` remains narrow
- no diff-shaped field appears

Rollback point:

- revert runtime path only, leave core intact

## Phase 3: replace directory analysis embedded writers

Goal:

- remove logging ownership from `DeletionDirectoryAnalysisService`

Tasks:

1. replace `PerFileTimingLog`
2. replace `PerFilePhaseTimingLogs`
3. replace `PerFileMemoryDiagnosticsLog`
4. route all of them through `AnalysisTextLogWriter`

Required result:

- directory analysis produces file, phase, and memory records through the shared sink
- service no longer owns file output schemas

Stop condition:

- custom writer classes are removed or fully bypassed
- one file completion still yields one stable event
- phase timings remain complete for the supported non-rewrite stage set

Rollback point:

- phase-local rollback is still possible because runtime logging is already independent

## Phase 4: add filterable public CLI surface

Goal:

- make the system usable rather than merely internally correct

Tasks:

1. add `--log-level`
2. add `--log-categories`
3. add `--log-view`
4. add `--log-profile`
5. add `--analysis-events-log`

Required first presets:

- `minimal`
- `normal`
- `diagnostic`
- `benchmark`

Stop condition:

- user can suppress noisy categories without code changes
- user can choose a benchmark-oriented view

Rollback point:

- the core pipeline still survives even if some CLI option naming is adjusted

## Phase 5: compatibility cleanup

Goal:

- stop carrying old topology forever

Tasks:

1. map old options to the new pipeline
2. deprecate per-phase directory fan-out
3. update docs and tests away from JSONL examples

Stop condition:

- there is one coherent text-log story
- legacy options have a clear fate

## Minimum File Touch List

The expected core file set is:

- `src/Host/RuntimeMetricsLog.cs`
- `src/Host/DeletionDirectoryAnalysisService.cs`
- `src/Host/DeletionCommandHost.cs`
- `src/Host/DeletionApplicationOptions.cs`
- `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

The new folder is:

- `src/Host/Logging/`

The expected documentation follow-up is:

- `docs/quick-start.md`
- `docs/developer-guide.md`

## What Must Be Preserved During Refactor

Do not regress these semantics while moving architecture:

- run-level sampling still exists
- successful file completion still yields one timing event
- supported phase timings still exist
- memory snapshots still exist
- completion and failure events still exist
- dry runs with `--no-diff` or `--skip-rewrite` do not gain diff payloads in logs

## Filter Model To Ship First

Ship the smallest useful filter set first.

### Required first filters

- level
- category
- profile

### Acceptable first omission

- event-type filter may land immediately after if it slows the first closure

Reason:

- level + category + profile already closes the real user needs
- event-type filtering is useful but not required to eliminate the current pain

## Render Views To Ship First

Keep four stable views:

- `compact`
- `normal`
- `diagnostic`
- `benchmark`

The render-plan direction is:

1. resolve the view once
2. compile field list once
3. reuse the same ordering and omission rules for every event

This is the NLog-style idea worth taking. It avoids dozens of `if` branches spread across emitters.

## Test Closure

The redesign is not closed by design text alone. It closes only with tests.

### Required test groups

#### Formatter tests

- stable header order
- quoted string escaping
- omission of missing optional fields
- stable numeric formatting

#### Filter tests

- level suppression
- category suppression
- profile defaulting
- explicit option override

#### Event contract tests

- `run.completed` stays narrow
- `cpg.summary` only contains CPG fields
- `mark.summary` only contains mark fields
- `memory.snapshot` does not drift into general summary fields

#### Boundary tests

- no diff text appears
- no diff summary fields appear
- no rewrite payload fields appear

#### Integration tests

- runtime `.log` path works
- directory `.log` path works
- one completed file still yields one completion record

## Verification Commands

Minimum verification sequence:

```powershell
pwsh -File .\init.ps1
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests"
```

Recommended extended verification after integration:

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false
```

If network-related `NU1900` warnings continue to pollute CLI smoke checks, record that as an environment-side verification limitation rather than silently weakening the log design acceptance rules.

## Done Definition

This execution document is satisfied only when all of these are true:

1. runtime and analysis logs are both text `.log`
2. JSONL is no longer the primary output format anywhere in the log path
3. business services no longer own custom writer classes
4. level and category filtering both work
5. views are deterministic
6. tests parse and assert the new line format
7. no diff content or diff-specific summary enters logs
8. the repo docs no longer teach JSONL as the logging target shape

## Non-Goals For This Execution Round

Do not widen this round into:

- console formatter redesign
- full diff subsystem redesign
- arbitrary user template language
- external logging package adoption
- broad performance retuning unrelated to the logging path

That widening is how a clean logging refactor turns into an unfinishable branch.

## Risks And Countermeasures

### Risk 1: architecture is clean but migration stalls

Cause:

- building the core without converting enough call sites

Countermeasure:

- runtime path and directory path must both be migrated in the same feature line

### Risk 2: logs become another oversized metrics dump

Cause:

- pushing every counter into `run.completed`

Countermeasure:

- keep category-specific summaries separate
- keep `run.completed` narrow in tests

### Risk 3: diff pressure leaks back into the log system

Cause:

- convenience requests to put diff summary in the same file

Countermeasure:

- hard test boundaries for forbidden diff fields
- keep diff subsystem outputs separate from `.log`

### Risk 4: too many options delay closure

Cause:

- trying to perfect the public CLI before the pipeline exists

Countermeasure:

- ship level/category/profile first
- expand only after the first text-log loop is stable

## Recommended Next Action

Implement against this document in this exact order:

1. tests for text-line contract
2. shared logging core
3. runtime path migration
4. directory path migration
5. CLI filter surface
6. compatibility cleanup

That sequence is the shortest path to a verifiable closed loop.
