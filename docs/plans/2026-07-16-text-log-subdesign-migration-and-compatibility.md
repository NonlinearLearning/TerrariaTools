# Text Log Subdesign: Migration And Compatibility

## Goal

Define how the repo moves from the current JSONL-oriented logging surface to the new text log system without breaking the entire host surface in one step.

This document is a child design of:

- `docs/plans/2026-07-16-text-log-system-design.md`

## Why This Subdesign Exists

The current behavior is locked in three places:

- code paths in `DeletionCommandHost` and `DeletionDirectoryAnalysisService`
- tests in `PipelineComponentTests`
- user-facing instructions in `docs/quick-start.md`

A good internal design is not enough. The repo needs an explicit migration path.

## Current Surface To Migrate

Existing options:

- `--runtime-metrics-log`
- `--per-file-timing-log`
- `--per-file-phase-timing-log-directory`
- `--per-file-memory-diagnostics-log`

Existing output assumptions:

- `.jsonl` files
- one JSON object per line
- one directory containing phase-specific JSONL files

Existing test assumptions:

- field names such as `filePath`, `elapsedMs`, `completedAtUtc`
- `JsonDocument.Parse(...)` based assertions

## Migration Principle

Keep semantic behavior first. Replace file content and internal architecture second.

Semantic behavior to preserve during migration:

- run-level sampling still happens
- completion and failure events still happen
- file-level timing still happens after successful file completion
- phase-level timing still exists
- file-level memory snapshots still exist
- no diff content or diff-specific summary is introduced into `.log` files during or after migration

What may change:

- file extension
- line format
- field names
- file topology

## Compatibility Strategy

Use a two-stage migration.

## Stage 1: Internal Unification

Goal:

- route old options into the new text event pipeline
- keep option names stable for one migration window

### Stage 1 behavior

#### `--runtime-metrics-log`

Keep the option name.

Change:

- output content changes from JSONL to text event lines
- diff text and diff-specific summary fields remain forbidden

#### `--per-file-timing-log`

Keep the option name temporarily.

Change:

- path now receives text events with category `phase` or `run`, depending on the chosen view and filters
- diff content and diff-specific summary fields remain forbidden

#### `--per-file-phase-timing-log-directory`

Deprecate the directory fan-out model.

Temporary compatibility choice:

- either reject it with a clear migration message
- or map it to a single `.log` file plus `phase=` field

Recommended direction:

- keep it for one migration window, but write one file per phase only if strict backward compatibility is temporarily required by tests
- remove it after the first stable text-log rollout

#### `--per-file-memory-diagnostics-log`

Keep the option name temporarily.

Change:

- output content changes to `memory.snapshot` text events
- diff content and diff-specific summary fields remain forbidden

## Stage 2: Public Surface Cleanup

Goal:

- introduce the coherent new option set
- deprecate the old names with clear messages

New options:

- `--runtime-log`
- `--analysis-log`
- `--log-level`
- `--log-categories`
- `--log-events`
- `--log-view`
- `--log-profile`

Old options become:

- compatibility aliases
- or explicit deprecated options

## Recommended Migration Table

| Old option | Stage 1 | Stage 2 |
| --- | --- | --- |
| `--runtime-metrics-log` | keep name, switch content to text events | alias to `--runtime-log` |
| `--per-file-timing-log` | keep name, switch content to text events | alias to `--analysis-log` with filtered profile |
| `--per-file-phase-timing-log-directory` | temporary compatibility only | remove or reject with migration help |
| `--per-file-memory-diagnostics-log` | keep name, switch content to text events | alias to `--analysis-log` with `memory` category |

## Test Migration Direction

### Current tests

Current tests parse JSON and assert raw field presence.

Examples:

- per-file timing test
- per-file memory diagnostics test
- runtime metrics log test

### Stage 1 test update

Replace JSON parsing assertions with text line parsing helpers.

Introduce a small test helper that:

- parses one `key=value` line
- handles quoted values
- returns a dictionary for assertions

### Test focus after migration

Tests should verify:

- number of lines
- event category and event type
- required fields
- absence of forbidden fields on narrow events
- filter behavior
- deterministic field order for the header
- absence of diff text, rewrite payloads, and diff-specific summary fields

## Documentation Migration Direction

### Required updates

When the implementation lands, update:

- `docs/quick-start.md`
- `docs/developer-guide.md` if it references the old logging shape

### Documentation changes

Replace:

- `.jsonl` examples
- JSON field descriptions
- phase-directory fan-out guidance

With:

- `.log` examples
- text event examples
- category and filter examples

## Deprecation Messaging

Deprecated options should not fail silently.

Preferred behavior:

- accept the option
- emit a warning event or console warning once
- mention the replacement option

Example:

```text
`--per-file-memory-diagnostics-log` is deprecated and will be replaced by `--analysis-log` with `--log-categories memory`.
```

## Compatibility Scope

What should remain compatible:

- command success and failure semantics
- run/file/phase/memory observability coverage
- non-destructive logging paths
- the hard boundary that diff output and diff-specific summaries stay outside `.log` files

What does not need to remain compatible:

- exact file names ending with `.jsonl`
- exact JSON field names
- exact phase-directory layout

## Risk Controls

### Risk 1: Tests become brittle again

Mitigation:

- test event semantics, not incidental formatting beyond the stable header and documented fields

### Risk 2: Legacy options remain forever

Mitigation:

- document stage boundaries now
- treat phase-directory compatibility as temporary

### Risk 3: Users lose benchmark workflows

Mitigation:

- preserve run sampling, file timing, phase timing, and memory snapshots in the new text system before removing old options

## Completion Conditions

This subdesign is complete when:

- there is a staged migration path
- old options have a defined fate
- tests and docs have a clear migration direction
- compatibility is defined in terms of semantics, not old JSON field names
