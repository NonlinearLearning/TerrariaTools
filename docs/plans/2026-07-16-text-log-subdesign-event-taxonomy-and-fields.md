# Text Log Subdesign: Event Taxonomy And Fields

## Goal

Define the event categories, event types, and stable field sets for the text log system.

This document is a child design of:

- `docs/plans/2026-07-16-text-log-system-design.md`

## Why This Subdesign Exists

The current system mixes run lifecycle, CPG metrics, mark summaries, memory snapshots, and file timing into a few oversized records.

The replacement must answer two questions clearly:

1. What kind of event is this?
2. Which fields are allowed on this kind of event?

## Category Model

The initial category set is:

- `run`
- `diag`
- `cpg`
- `mark`
- `phase`
- `memory`
- `io`

Each event belongs to exactly one category.

## Event Type Model

The initial event type set is:

- `started`
- `sampled`
- `completed`
- `failed`
- `summary`
- `snapshot`

Each event belongs to exactly one event type.

The pair `category + eventType` defines the semantic meaning of the record.

## Common Header Fields

Every log line starts with the same fixed header fields in this order:

```text
ts lvl cat evt msg run
```

Field definitions:

- `ts`: UTC timestamp in ISO 8601
- `lvl`: `ERROR|WARN|INFO|DEBUG|TRACE`
- `cat`: category id
- `evt`: event type id
- `msg`: short fixed message
- `run`: run identifier

This keeps the front half of the line human-first, similar to how zap's console encoder prints core entry metadata as plain text before appending structured context.

## Shared Context Fields

These fields are optional and appear after the header when relevant:

- `op`
- `inputKind`
- `inputPath`
- `src`
- `file`
- `phase`
- `dop`

Rules:

- omit absent fields
- do not print placeholder values
- keep stable ordering when a field is present

`src` is the logical source/component name. It is the text-log equivalent of a logger name. It should be used for components such as:

- `host.runtime`
- `host.analysis`
- `cpg.builder`
- `rule.mark`

## Stable Category Contracts

### `run`

Allowed event types:

- `started`
- `sampled`
- `completed`
- `failed`

#### `run.started`

Purpose:

- declare a new command execution

Allowed fields:

- `op`
- `inputKind`
- `inputPath`
- `dop`

#### `run.sampled`

Purpose:

- periodic run-level snapshot

Allowed fields:

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

This event is intentionally denser than `run.completed`. The current runtime benchmark workflow depends on a periodic combined sample more than on a pure lifecycle marker.

#### `run.completed`

Purpose:

- top-level completion summary

Allowed fields:

- `op`
- `inputKind`
- `files`
- `elapsedMs`
- `edits`
- `diags`
- `status`

Forbidden fields:

- `nodes`
- `edges`
- `heapBytes`
- `cacheHits`
- any category-specific metric from `cpg`, `mark`, `memory`, or `io`

#### `run.failed`

Purpose:

- top-level failure summary

Allowed fields:

- `op`
- `inputKind`
- `elapsedMs`
- `status`
- `errorType`
- `error`

### `diag`

Allowed event types:

- `summary`
- `warning`
- `error`

#### `diag.summary`

Purpose:

- aggregated diagnostics summary

Allowed fields:

- `diags`
- `warnings`
- `errors`

#### `diag.warning`

Purpose:

- one warning event

Allowed fields:

- `file`
- `diagId`
- `span`
- `text`

#### `diag.error`

Purpose:

- one error event

Allowed fields:

- `file`
- `diagId`
- `span`
- `text`

### `cpg`

Allowed event types:

- `summary`

#### `cpg.summary`

Purpose:

- CPG-wide summary after a run or file

Allowed fields:

- `nodes`
- `edges`
- `partitions`
- `opMs`
- `syntaxMs`
- `dataFlowMs`
- `freezeMs`

Optional extended fields for high-detail views:

- `syntaxNodes`
- `syntaxTokens`
- `candidateEdges`
- `skippedMethods`

### `mark`

Allowed event types:

- `summary`

#### `mark.summary`

Purpose:

- mark-wide summary

Allowed fields:

- `rules`
- `slowestRule`
- `slowestMs`
- `cacheHits`
- `cacheMisses`

Optional extended fields for high-detail views:

- `atomicCandidateHits`
- `atomicCandidateMisses`
- `graphBindingHits`
- `graphBindingMisses`
- `sliceCacheHits`
- `sliceCacheMisses`

### `phase`

Allowed event types:

- `completed`

#### `phase.completed`

Purpose:

- file-level phase timing

Required fields:

- `file`
- `phase`
- `elapsedMs`

Allowed phase values:

- `semantic-model`
- `cpg-build`
- `mark`
- `propagate`
- `lift`
- `decide`
- `total`

### `memory`

Allowed event types:

- `snapshot`

#### `memory.snapshot`

Purpose:

- memory and allocation snapshot at run or file boundary

Allowed fields:

- `file`
- `elapsedMs`
- `allocBytes`
- `heapBytes`
- `committedBytes`
- `fragmentedBytes`
- `wsBytes`
- `privateBytes`

Optional extended fields:

- `gen2Collections`
- `tpThreads`
- `tpPending`

### `io`

Allowed event types:

- `summary`
- `failed`

Allowed fields:

- `path`
- `records`
- `flushMs`
- `errorType`
- `error`

## Field Naming Rules

- use ASCII-only names
- use lower camel-like compact names already used in the repo, such as `elapsedMs`
- keep units in the field name when needed
- prefer short names over prose names
- preserve one stable meaning per field name

## Forbidden Patterns

- category-specific fields on `run.completed`
- dumping every raw counter into every event
- one-off field names that appear in only one code path without category documentation
- nested payload syntax
- fields whose meaning changes between views
- any full diff text, diff hunk text, original text, or replacement text
- any diff-specific summary field such as `diff`, `diffSummary`, `diffFiles`, or `diffBlocks`

## Completion Conditions

This subdesign is complete when:

- every emitted event can be classified by `cat + evt`
- each category has a documented field whitelist
- `run.completed` remains narrow
- high-detail counters are isolated to detail-capable categories and views
