# Text Log Subdesign: Filtering And Views

## Goal

Define how users choose which events are written and which fields each event renders.

This document is a child design of:

- `docs/plans/2026-07-16-text-log-system-design.md`

## Why This Subdesign Exists

Level-only filtering is too weak for this repo.

The real use cases are:

- keep only run summaries
- include CPG and mark summaries but exclude memory snapshots
- keep detailed file phase timings for benchmark work
- keep only failures and warnings during normal development

That requires filtering by more than severity.

## Filtering Dimensions

The text log system uses four filtering dimensions:

1. level
2. category
3. event type
4. view

This is the first version. A later extension may add source/component filtering through the shared `src` field, but it is not required for the initial rollout.

## Level Filter

### Option

```text
--log-level ERROR|WARN|INFO|DEBUG|TRACE
```

### Purpose

Suppress low-priority events before any expensive detail rendering.

### Rules

- case-insensitive input
- normalized to upper-case internal representation
- invalid values fail fast

### Default

```text
INFO
```

## Category Filter

### Option

```text
--log-categories run,diag,cpg
```

### Purpose

Decide which subsystems can emit records.

### Rules

- comma-separated input
- case-insensitive input
- normalized to lower-case category ids
- unknown categories fail fast
- empty category items are ignored

### Default

No explicit category filter means "use the categories implied by the selected profile."

## Event Type Filter

### Option

```text
--log-events completed,summary,failed
```

### Purpose

Allow users to keep only lifecycle summaries or only snapshots without rewriting category rules.

### Rules

- comma-separated input
- case-insensitive input
- normalized to lower-case ids
- unknown event types fail fast

### Default

No explicit event-type filter means "use all event types implied by the selected profile."

## View Filter

### Option

```text
--log-view compact|normal|diagnostic|benchmark
```

### Purpose

Choose how many fields each event prints.

This is the replacement for the rejected JSONL payload model and the replacement for arbitrary user-defined templates in the first version.

The implementation direction should follow NLog `SimpleLayout` more than ad-hoc string concatenation:

- compile a view into a render plan once
- reuse that render plan for every event
- avoid per-event branching for fields that the selected view never prints

## Why Views Exist

Category and event filtering decide whether an event is emitted.

View selection decides how much of that event is rendered.

Without views, the system has only two bad choices:

- always print too much
- always hide useful detail

## View Definitions

### `compact`

Use when the user wants a stable, short execution log.

Header:

- `ts`
- `lvl`
- `cat`
- `evt`
- `msg`
- `run`

Typical extra fields:

- `elapsedMs`
- `status`
- `files`
- `diags`

`compact` should never print detailed cache counters, detailed memory values, or extended CPG internals.

### `normal`

Use for day-to-day engineering work.

Fields allowed beyond `compact`:

- `op`
- `inputKind`
- `file`
- `phase`
- `dop`
- stable category summary fields such as `nodes`, `edges`, `rules`, `slowestRule`, `heapBytes`

`normal` should still avoid raw counter floods.

### `diagnostic`

Use when debugging performance, memory, or pipeline behavior.

Fields allowed:

- all `normal` fields
- raw counter fields documented in the event taxonomy design
- ThreadPool and GC counters
- per-phase detail fields

### `benchmark`

Use for repeatable performance runs.

Priority:

- timing and memory over prose
- stable summary fields over rare detail fields

Fields preferred:

- `elapsedMs`
- `dop`
- `nodes`
- `edges`
- `syntaxMs`
- `dataFlowMs`
- `freezeMs`
- `heapBytes`
- `privateBytes`

Fields suppressed when possible:

- verbose diagnostic text
- individual warnings unless `WARN` or `ERROR`

## Profile Filter

### Option

```text
--log-profile minimal|normal|diagnostic|benchmark
```

### Purpose

Give users a single high-level preset instead of requiring four separate flags every time.

Profiles define defaults for:

- level
- categories
- event types
- view

## Profile Definitions

### `minimal`

- level: `INFO`
- categories: `run,diag`
- event types: `started,completed,failed,summary,error,warning`
- view: `compact`

### `normal`

- level: `INFO`
- categories: `run,diag,cpg,mark`
- event types: `started,completed,failed,summary,error,warning`
- view: `normal`

### `diagnostic`

- level: `DEBUG`
- categories: `run,diag,cpg,mark,phase,memory,io`
- event types: all
- view: `diagnostic`

### `benchmark`

- level: `DEBUG`
- categories: `run,cpg,mark,phase,memory,io`
- event types: `started,sampled,completed,failed,summary,snapshot`
- view: `benchmark`

## Resolution Order

Resolution is deterministic:

1. start from built-in defaults
2. apply `--log-profile` if present
3. apply explicit `--log-level`
4. apply explicit `--log-categories`
5. apply explicit `--log-events`
6. apply explicit `--log-view`

Explicit values always win over profile defaults.

## Rendering Policy

Filtering should happen in this order:

1. level check
2. category check
3. event-type check
4. event construction or field enrichment
5. view-specific rendering

This ordering keeps hot paths closer to zap's "check before write" discipline.

## Why Not Full Templates Yet

This repo does not need a user-defined template DSL in the first version.

Reasons:

- field semantics are still being stabilized
- free-form templates would explode test combinations
- the current need is controlled selection, not arbitrary formatting

This also avoids opening a large compatibility surface too early. Serilog and NLog both show that template/layout systems are powerful, but they become long-lived contracts once exposed.

## Sink-Scoped Filtering

The first version should support different filters per sink, even if the CLI exposes only a simple global surface at first.

Reason:

- Serilog supports restricted levels per sink
- zerolog supports writer composition and level-aware writers

That maps well to this repo because `runtime.log` and `analysis.log` have different workloads.

Recommended internal model:

- global filter from CLI options
- optional sink-local narrowing filter

Example:

- runtime sink keeps `run,cpg,mark,diag`
- analysis sink keeps `file,phase,memory,io`

Hard boundary:

- no `diff` or `rewrite` log category exists
- filtering is only for runtime observability events
- diff output selection belongs to the diff subsystem, not to `--log-categories`, `--log-events`, or `--log-view`

If template support is needed later, it should be layered on top of stable categories, event types, and views.

## Future Extension Point

If field-level selection becomes necessary later, add a narrow option like:

```text
--log-fields run:files,elapsedMs,diags;cpg:nodes,edges,freezeMs
```

This should remain a later extension, not a day-one requirement.

Another acceptable later extension is:

```text
--log-sources host.runtime,cpg.builder
```

That should be built on top of the shared `src` field instead of inventing new category ids.

## Completion Conditions

This subdesign is complete when:

- users can filter by level, category, and event type
- users can choose a stable output view
- profiles define meaningful presets
- filtering order is deterministic and cheap
