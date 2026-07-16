# Text Log System Design

## Goal

Replace the current ad-hoc metrics and per-file JSONL emitters with one text-based `.log` event system that:

- keeps machine-stable one-line records without using JSON/JSONL
- separates run, subsystem, file, phase, and failure events
- supports output filtering by level and category
- keeps the CLI text formatter out of scope for this change

## Scope

This design covers only runtime and diagnostic log files produced by:

- `--runtime-metrics-log`
- per-file timing and memory diagnostics
- per-file phase timing output

This design does not cover:

- `DeletionResultFormatter` console output
- user-facing diff formatting
- external logging dependencies
- any full diff body, diff hunk text, or per-edit replacement payload in `.log` files

## Current Problems

### 1. Event model is missing

`src/Host/RuntimeMetricsLog.cs` builds anonymous JSON objects directly.

`src/Host/DeletionDirectoryAnalysisService.cs` embeds three unrelated log writers:

- `PerFileTimingLog`
- `PerFilePhaseTimingLogs`
- `PerFileMemoryDiagnosticsLog`

Each writer owns its own schema, field names, buffering rules, and lifecycle.

### 2. Event responsibilities are mixed

The current terminal metrics record tries to carry:

- run lifecycle
- performance summary
- CPG summary
- mark summary
- structure-view summary

That makes filtering weak and output noisy.

### 3. Content filtering is effectively absent

Today the user can choose which files are written, but cannot cleanly say:

- show only run-level summaries
- include CPG and mark summaries but exclude memory snapshots
- include only warnings and errors

### 4. Logging code is scattered through business classes

The directory analysis service owns file writers and output schemas. That makes later changes risky and encourages more inline formatting.

## External Design Direction

The selected direction combines four patterns from the reviewed projects:

- Serilog: event pipeline and sink separation
- zap: small core logging contract and explicit write/flush lifecycle
- zerolog: writer composition and pooled event discipline on hot paths
- NLog `SimpleLayout`: text layout rendered from stable event fields

The design intentionally does not follow zerolog's JSON-first direction because this repo rejects JSON/JSONL log content.

## Hard Boundary: No Diff In Logs

The text log system must not carry diff content or diff summaries.

Forbidden in `.log` files:

- full diff text
- unified diff hunks
- legacy `DiffText`
- per-edit original text
- per-edit replacement text
- multi-line diff payloads

Allowed relationship:

- the log system may record only general execution facts such as `edits=12`
- full diff rendering and any diff-specific summary remain outside the log system

Forbidden even in single-line form:

- `diff=*`
- `diffSummary=*`
- per-file diff counts that are defined by diff rendering shape

This boundary is strict. The log system is for runtime observability, not for transporting or summarizing diff artifacts.

## Chosen Format

Use one-line text events in `.log` files.

Each record is a flat `key=value` line with fixed leading fields and category-specific trailing fields.

Example:

```text
ts=2026-07-16T12:52:10.000Z lvl=INFO cat=run evt=completed msg="analysis completed" run=01K0M op=delete-class inputKind=directory files=1503/1503 elapsedMs=1033075 edits=0 diags=0
ts=2026-07-16T12:52:10.005Z lvl=DEBUG cat=cpg evt=summary msg="cpg build summary" run=01K0M nodes=2108977 edges=5322409 partitions=16 opMs=27 syntaxMs=211 dataFlowMs=24 freezeMs=48
ts=2026-07-16T12:52:10.006Z lvl=DEBUG cat=mark evt=summary msg="mark summary" run=01K0M rules=23 slowestRule=DEL-DEAD-001 slowestMs=7 cacheHits=0 cacheMisses=0
ts=2026-07-16T12:52:10.007Z lvl=TRACE cat=phase evt=file.completed msg="phase completed" run=01K0M file="Terraria\\NPC.cs" phase=mark elapsedMs=217
ts=2026-07-16T12:52:10.008Z lvl=TRACE cat=memory evt=file.snapshot msg="memory snapshot after file" run=01K0M file="Terraria\\NPC.cs" heapBytes=13924952952 wsBytes=11190898688 privateBytes=10670821376
```

## Format Rules

### Line model

- one event per line
- UTF-8 text
- no multi-line payloads
- no mixed prose and structured blobs

### Required field order

Every event begins with these fields in this exact order:

```text
ts lvl cat evt msg run
```

After that, context fields and category-specific fields follow.

### Encoding rules

- strings with spaces or separators use double quotes
- integers and floating-point values are emitted without quotes
- booleans use `true` or `false`
- missing optional values are omitted, not printed as `null`

### Stability rules

- key names are stable once released
- category-specific fields keep deterministic ordering
- field names stay ASCII-only

## Event Model

### Common fields

Every event may use these shared fields:

- `ts`: UTC timestamp in ISO 8601
- `lvl`: `ERROR|WARN|INFO|DEBUG|TRACE`
- `cat`: event category
- `evt`: event type inside the category
- `msg`: short fixed message
- `run`: stable run identifier
- `op`: logical operation such as `delete-class`
- `inputKind`: `demo|single-file|directory`
- `inputPath`: root input path when relevant
- `file`: file path for file-scoped events
- `phase`: stage name for phase-scoped events
- `dop`: effective max degree of parallelism when relevant

### Categories

The initial category set is:

- `run`
- `file`
- `phase`
- `memory`
- `cpg`
- `mark`
- `diag`
- `io`

New categories must be added deliberately and documented.

### Event types by category

#### `run`

- `started`
- `sampled`
- `completed`
- `failed`

#### `file`

- `completed`

#### `diag`

- `summary`
- `error`
- `warning`

#### `cpg`

- `summary`

#### `mark`

- `summary`

#### `phase`

- `file.completed`

#### `memory`

- `file.snapshot`
- `run.sampled`

#### `io`

- `summary`
- `writer.failed`

## Event Content Design

### `run.completed`

Keep only top-level outcome fields:

- `files`
- `elapsedMs`
- `edits`
- `diags`
- `status`

Do not embed CPG, mark, memory, or cache summaries here.
Do not embed diff text or edit payloads here.

Example:

```text
ts=... lvl=INFO cat=run evt=completed msg="analysis completed" run=... op=delete-class inputKind=directory files=1503/1503 elapsedMs=1033075 edits=0 diags=0 status=completed
```

### `run.sampled`

Carry the periodic runtime sample fields that current benchmark-style workflows actually need:

- `elapsedMs`
- `allocBytes`
- `gen0`
- `gen1`
- `gen2`
- `heapBytes`
- `wsBytes`
- `tpThreads`
- `tpPending`
- `tpCompleted`
- `availableWorkers`
- `maxWorkers`

### `cpg.summary`

Carry only CPG-wide summary fields:

- `nodes`
- `edges`
- `partitions`
- `opMs`
- `syntaxMs`
- `dataFlowMs`
- `freezeMs`

### `mark.summary`

Carry only mark-wide summary fields:

- `rules`
- `slowestRule`
- `slowestMs`
- `cacheHits`
- `cacheMisses`

This first version can keep aggregated cache totals instead of every raw counter. Raw counters can remain available behind `TRACE` if still needed later.

### `file.completed`

Carry only per-file completion timing:

- `file`
- `elapsedMs`

This event must not include per-file diff bodies or replacement text.

### `phase.completed`

Carry only phase timing:

- `file`
- `phase`
- `elapsedMs`

The first version should preserve the current non-rewrite phase set:

- `semantic-model`
- `cpg-build`
- `mark`
- `propagate`
- `lift`
- `decide`
- `total`

### `memory.file.snapshot`

Carry only memory-related measurements:

- `file`
- `heapBytes`
- `committedBytes`
- `fragmentedBytes`
- `wsBytes`
- `privateBytes`
- `allocBytes`

## Filtering Design

Level-only filtering is not enough. This system needs category filtering too.

### Option 1: level filter

```text
--log-level ERROR|WARN|INFO|DEBUG|TRACE
```

Default:

```text
INFO
```

### Option 2: category filter

```text
--log-categories run,diag,cpg
```

Rules:

- comma-separated
- case-insensitive input
- normalized to lower-case category ids
- unknown categories fail fast with a clear error

### Option 3: profile filter

```text
--log-profile minimal|normal|diagnostic|benchmark
```

Profiles:

- `minimal`
  - categories: `run,diag`
  - level: `INFO`
- `normal`
  - categories: `run,file,diag,cpg,mark`
  - level: `INFO`
- `diagnostic`
  - categories: `run,file,phase,memory,cpg,mark,diag,io`
  - level: `DEBUG`
- `benchmark`
  - categories: `run,file,phase,memory,cpg,mark,io`
  - level: `DEBUG`

### Resolution order

1. apply `--log-profile` defaults if present
2. apply explicit `--log-level` if present
3. apply explicit `--log-categories` if present

Explicit options always win over profile defaults.

## File Strategy

Keep `.log` files, but reduce the number of unrelated formats.

### Preferred outputs

- `--runtime-metrics-log <path>`
  - emits `run`, selected `cpg`, selected `mark`, and selected `diag` events
- `--analysis-events-log <path>`
  - emits `file`, `phase`, `memory`, and `io` events

Neither sink may carry diff text or diff-specific summary fields.

### Compatibility

Legacy switches can stay for one migration window:

- `--per-file-timing-log`
- `--per-file-phase-timing-log-directory`
- `--per-file-memory-diagnostics-log`

But internally they should map into the new event pipeline instead of preserving separate writer classes.

## Internal Architecture

### New types

Add a small logging surface under `src/Host/Logging/`:

- `TextLogEvent`
- `TextLogLevel`
- `TextLogCategory`
- `ITextLogSink`
- `TextLogFormatter`
- `TextLogFilter`
- `RunLogContext`

### Sink contract

`ITextLogSink` should stay minimal:

- `Emit(TextLogEvent logEvent)`
- `Flush()`

Optional dispose support is allowed for file-backed sinks.

### Formatter contract

`TextLogFormatter` owns:

- field ordering
- escaping
- omission of missing values
- deterministic rendering of category-specific fields

Business classes must not render log lines directly.

### Filter contract

`TextLogFilter` decides:

- whether a level is enabled
- whether a category is enabled

Filtering happens before expensive payload construction where possible.

### File sink

Use one async file writer with one bounded queue per sink instance.

The sink owns:

- file creation
- background write loop
- final flush
- failure propagation

The sink does not own:

- GC sampling
- ThreadPool sampling
- `PrototypeAnalysisResult` inspection

## Minimum Refactor Plan

### Step 1

Introduce the new logging abstractions under `src/Host/Logging/`.

### Step 2

Replace `RuntimeMetricsLog` with a run-scoped text event logger that emits:

- `run.started`
- `run.sampled`
- `run.completed`
- `run.failed`
- optional `cpg.summary`
- optional `mark.summary`

### Step 3

Remove embedded writer classes from `DeletionDirectoryAnalysisService` and route all file/phase/memory events through the shared text sink.

### Step 4

Extend `DeletionApplicationOptions` with:

- `--log-level`
- `--log-categories`
- `--log-profile`
- `--analysis-events-log`

### Step 5

Map legacy per-file logging options onto the new pipeline or deprecate them with clear diagnostics.

## Verification

Minimum verification for the implementation phase:

```powershell
pwsh -File .\init.ps1
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests"
```

Additional targeted tests should cover:

- line format stability
- escaping and quoting rules
- level filtering
- category filtering
- profile override order
- `run.completed` not containing unrelated subsystem fields
- `cpg.summary` and `mark.summary` emission only when enabled
- `file.completed` preserving one record per completed file
- `phase.completed` preserving the current seven non-rewrite stages

## Completion Conditions

This design is implemented only when:

- no runtime or per-file log writer still emits JSON/JSONL content
- logging code is no longer embedded as custom writer classes inside `DeletionDirectoryAnalysisService`
- category and level filtering both work
- top-level completion events stay narrow and do not embed unrelated subsystem summaries
- no diff text, diff-specific summary field, hunk text, or per-edit rewrite payload enters `.log` files
- tests lock line shape and filter behavior

## Risks

- Too many categories will recreate the current clutter in a different shape.
- Keeping all raw counters at `INFO` or `DEBUG` will still produce noisy logs.
- Legacy option compatibility may delay cleanup if old file layouts are preserved too long.

## Recommendation

Start with the narrow category set in this document, default to `normal`, and keep detailed file and memory events behind `diagnostic` or explicit category selection.
