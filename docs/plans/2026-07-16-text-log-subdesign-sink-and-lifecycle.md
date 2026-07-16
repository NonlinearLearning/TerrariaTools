# Text Log Subdesign: Sink And Lifecycle

## Goal

Define how text log events are buffered, written, flushed, and failed.

This document is a child design of:

- `docs/plans/2026-07-16-text-log-system-design.md`

## Why This Subdesign Exists

The current repo has log writing logic embedded directly inside:

- `RuntimeMetricsLog`
- `DeletionDirectoryAnalysisService.PerFileTimingLog`
- `DeletionDirectoryAnalysisService.PerFilePhaseTimingLogs`
- `DeletionDirectoryAnalysisService.PerFileMemoryDiagnosticsLog`

That mixes business logic, file I/O, buffering, and output schema.

The replacement should follow a small-core sink model closer to Serilog sinks and zap `Write/Sync`.

It should also borrow zerolog's discipline of building an event once, writing it once, and then releasing temporary buffers.

## Target Structure

Add a shared logging surface under `src/Host/Logging/`.

Proposed types:

- `TextLogEvent`
- `RunLogContext`
- `ITextLogSink`
- `ITextLogFormatter`
- `TextLogFilter`
- `TextLogFileSink`
- `RunTextLogWriter`
- `AnalysisTextLogWriter`
- `PooledTextLogEventBuilder`

## Core Contracts

### `ITextLogSink`

The sink contract should stay small:

- `Emit(TextLogEvent logEvent)`
- `Flush()`

Optional disposal support is allowed:

- `Dispose()`
- `DisposeAsync()`

The sink is responsible only for accepting already-built events and writing rendered lines.

### `ITextLogFormatter`

The formatter contract owns:

- field ordering
- quoting
- escaping
- omission of absent fields
- view-specific field selection

The formatter must not:

- sample GC state
- inspect `PrototypeAnalysisResult`
- manage background tasks

Implementation direction:

- precompile per-view render plans
- write directly into a reusable buffer when possible
- keep field ordering logic out of producers

## Sink Topology

### Runtime sink

Purpose:

- write run-level, summary-level, and failure-level events

Expected output:

- one `.log` file

Producer examples:

- command start
- periodic run sampling
- command completion
- command failure
- optional CPG summary
- optional mark summary

### Analysis sink

Purpose:

- write file-level and phase-level events

Expected output:

- one `.log` file

Producer examples:

- file timing
- phase completion
- file memory snapshot
- writer failures

## Why Separate Runtime And Analysis Sinks

The repo has two distinct workloads:

- low-frequency run events
- high-frequency file and phase events

Keeping them in separate sinks gives:

- cleaner filtering defaults
- separate file sizes
- lower risk of summary events being drowned by file events

## Writer Model

Each sink instance uses:

- one bounded queue
- one background writer task
- one formatter
- one file stream

This retains the current asynchronous write advantage while removing the duplicated writer classes.

## Event Builder Reuse

High-frequency events such as file completion and memory snapshots should avoid unbounded temporary allocations.

Recommended direction:

- use a pooled field buffer or pooled event builder
- build the final immutable event before enqueue
- release temporary buffers immediately after the rendered line is produced or after the immutable event is copied

This takes the useful part of zerolog's pooled event pattern without adopting its JSON-first API.

## Queue Policy

Recommended queue behavior:

- bounded capacity
- single reader
- multiple writers
- backpressure instead of silent dropping

Why:

- silent drops are unacceptable for diagnostics
- unbounded growth is unsafe during large directory runs

Initial direction:

- `BoundedChannelFullMode.Wait`

## Flush Policy

### Required behavior

- `Flush()` forces all enqueued records to be written
- disposal performs a final flush
- normal command completion waits for sink drain

### Failure behavior

If the background writer faults:

- stop accepting new events
- surface the original exception to the caller
- write a best-effort in-memory failure state if possible
- do not continue pretending logging succeeded

This follows the spirit of explicit `Sync()` from zap more than "best effort and forget."

## Writer Composition

The sink layer should allow composition similar to zerolog writer wrappers or Serilog sink chaining, but keep the first version small.

Useful composition points:

- file sink
- tee/multi sink
- sink-local filter wrapper
- sync wrapper for tests

The first rollout does not need a public composition DSL. Internal composition is enough.

## Lifecycle Hooks

### Command host lifecycle

`DeletionCommandHost` should own run-level sink setup and teardown.

Lifecycle:

1. parse options
2. build filter and formatter
3. create sink(s)
4. emit `run.started`
5. execute analysis
6. emit `run.completed` or `run.failed`
7. flush and dispose sinks

### Directory analysis lifecycle

`DeletionDirectoryAnalysisService` should not own custom writer classes anymore.

It should only receive an analysis log writer abstraction and call methods such as:

- `WriteFileCompleted(...)`
- `WritePhaseCompleted(...)`
- `WriteMemorySnapshot(...)`

The service remains a producer, not a sink owner.

## Sampling Ownership

Sampling should stay outside the sink.

The sink does not know:

- when a run sample should happen
- how GC metrics are collected
- when a file is considered completed

Those decisions belong to producer-side writers:

- `RunTextLogWriter`
- `AnalysisTextLogWriter`

## Error Ownership

Sink layer owns:

- file open failure
- file write failure
- flush failure
- background task failure

Producer layer owns:

- invalid event semantics
- missing required fields
- misuse of category or event type

Formatter layer owns:

- invalid quoting
- invalid rendering
- field ordering mistakes

## Minimal Replacement Map

### Replace `RuntimeMetricsLog`

Current role:

- timer
- runtime sampling
- JSON serialization
- file stream ownership
- terminal summary rendering

New split:

- `RunTextLogWriter`: timing, sampling, event construction
- `TextLogFileSink`: queue, background write, flush
- `TextLogFormatter`: line rendering

### Replace embedded per-file writers

Current role:

- per-file queue ownership
- per-phase file fan-out
- memory snapshot rendering

New split:

- `AnalysisTextLogWriter`: event construction for file, phase, and memory categories
- `TextLogFileSink`: shared output path and writer lifecycle

## Recommended File Count

First version should keep file count small:

- one runtime log
- one analysis log

Do not recreate the old seven-file phase split inside the new system.

Phase remains a field, not a file topology.

## Concurrency Direction

The sink itself does not need complex parallel behavior.

Key properties:

- multi-producer safe
- single ordered writer
- deterministic per-sink output order according to enqueue order

For file and phase events, ordering by enqueue time is enough. The system does not need cross-thread total ordering stronger than that in the first version.

The hot path should follow zap's discipline:

1. cheap enabled/filter check
2. event construction only when enabled
3. enqueue
4. explicit flush at lifecycle boundaries

## Completion Conditions

This subdesign is complete when:

- no business service owns custom file-writer classes
- runtime and analysis sinks are shared abstractions
- queue, flush, and failure behavior are explicitly defined
- producer, formatter, and sink responsibilities are separated
