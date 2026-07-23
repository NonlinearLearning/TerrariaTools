# CPG And Directory DOP Split Design

## Goal

Measure directory-level and per-file CPG partition parallelism independently, without changing the current CLI behavior when the new option is absent.

## Chosen Interface

Keep `--max-degree-of-parallelism` as the directory scheduler limit and existing default source. Add an optional `--cpg-max-degree-of-parallelism <positive-int>` override for the CPG builder.

When the override is absent, the builder continues to receive the resolved global DOP. This preserves all existing invocations and tests. The option must be rejected when it is zero, negative, or non-numeric.

## Execution Model

`DeletionDirectoryAnalysisService` continues to schedule files with the runtime directory DOP. `DeletionApplicationService` resolves the CPG DOP once per analysis and passes it only to `RoslynCpgBuilderOptions.MaxDegreeOfParallelism`. The runtime rule scheduler, group parallelism, and helper parallelism retain their current behavior.

The benchmark matrix uses the same input, rule options, logging profile, SDK, and process setup:

| Case | Directory DOP | CPG DOP |
| --- | ---: | ---: |
| Directory-only | 12 | 1 |
| CPG-only | 1 | 12 |
| Nested baseline | 12 | 12 |

Run one warmup and three measured executions per case. Record wall-clock time, completed-file count, phase aggregates, managed allocation, GC collections, managed heap, working set, and ThreadPool counters. Treat the three phase aggregates as summed per-file elapsed times; use wall-clock for end-to-end ranking.

## Correctness And Rollback

Add focused option-flow tests proving the absent override inherits the global DOP and the explicit override reaches only the CPG builder. Preserve graph, rule result, and rewrite equivalence across DOP values. The default remains unchanged until repeated measurements show a stable outcome.

Rollback consists of removing the optional override path. Existing `--max-degree-of-parallelism` behavior remains the fallback contract.
