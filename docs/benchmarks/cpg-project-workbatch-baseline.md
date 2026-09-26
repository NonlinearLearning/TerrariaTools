# CPG Project WorkBatch Baseline

Status: implementation in progress. This page records the project-level
telemetry contract and verification boundary; it is not a performance claim.

## Execution model

- The exporter uses one bounded project queue and one stable result reducer.
- Production admission is capped at 12 project workers.
- A project worker configures the NLCPG builder with local DOP 1 and synchronous
  local WorkBatch execution, so the project pool is the only concurrent batch
  scheduler.
- Valid resume payloads are imported by the catalog path. Missing or invalid
  payloads are the only documents admitted to the project CPU queue.
- Project admission defaults to a 64 MiB total estimated-memory budget, with
  an 8 MiB Roslyn/project baseline reservation. Queue, active-result, and
  completed-not-reduced estimates consume the remaining admission budget; the
  values are scheduling estimates, not CLR working-set guarantees.
- Payload writes use a per-file temporary path and atomic replacement; SQLite
  catalog writes happen in the stable reducer. A successful manifest is
  written to `manifest.json.tmp` and atomically moved into place only after
  catalog projection completes.

## Telemetry fields

Each project batch event is exposed through `ProjectExportResult.ProjectTelemetry`
and carries `RunId`, stable stage and batch identities,
worker count, worker task count, active worker count, active ratio, queue and
reserved estimated bytes, completed-not-reduced high-water values, processing
time, reducer wait, and runtime pause milliseconds sampled from
`GC.GetTotalPauseDuration()` while a worker is blocked at a bounded wait.
Idle reasons are represented explicitly by `ProjectCpgWorkBatchIdleReason`:
`NoRunnableBatch`, `WaitingForFragmentSink`, `WaitingForCatalog`,
`RoslynBarrier`, `GcOrRuntime`, `Cancelled`, and `Shutdown`.

Catalog write and manifest finalize durations remain separate fields so a low
CPU sample during persistence or finalization is not interpreted as a CPG
calculation failure.

## Small-sample verification

On 2026-09-22, the repository serial wrapper ran a focused synthetic project
pool sample with 120 project batches (`12 workers x 10 batches`) and 12 workers:

```text
workers=12; workerTasks=12; peakActive=12; activeRatio=0.850;
elapsedMs=166; activeWorkerMs=1694
```

The focused test passed `1/1`. The input count in
`ProjectCpgWorkBatchExecutorContractTests` is now defined as
`workerCount * batchesPerWorker` with `batchesPerWorker = 10`.
The real `NLCPG.ProjectExport` App fixture then passed the project-pool
concurrency and cancellation contract selection `2/2`. It verified a single
capped project pool, 12 worker/task identities in project telemetry, a shutdown
telemetry event, and no manifest temporary file after pre-cancelled export.

This is scheduler and lifecycle evidence, not an OS CPU utilization or
throughput claim. The synthetic callback sleeps for 5 ms, so the `0.850`
worker-active ratio still includes startup, queue, reducer, and shutdown
overhead. Increasing the input from 24 to 120 batches improved sample
occupancy, while `peakActive=12` remained stable.
The App fixture is intentionally small and does not represent the Version4
930-document workload. The broader project-pool verification is recorded
below; DOP equivalence and Version4 acceptance remain separate boundaries.

## Contract and Performance verification

On 2026-09-21, the affected project-pool Contract selections passed through
the repository serial wrapper:

- `CpgProjectWorkBatchPoolContractTests`: `8/8`;
- combined project planner, batch worker, determinism, exporter,
  concurrency, and persistence selection: `21/21`;
- existing single-document `CpgWorkBatch` Contract selection: `49/49`.

The project-pool Performance selection passed `5/5`, covering the fixed-worker
executor, stable planner order, telemetry queue metrics, and fail-open
telemetry sink. The complete Performance project passed `77/77` runnable tests
with `5` external Terraria tests skipped because
`NLISSN_RUN_TERRARIA_EXTERNAL_TESTS` was not enabled; it exited successfully.

The affected project builds also passed: NLCPG, ProjectExport, and Performance
with `0 warning/0 error`; Contract with `0 error` and the existing Verify
warning about solution auto-discovery. Harness consistency and
`git diff --check` both passed.

These results verify the current Contract/Performance boundary, not DOP1/2/12
semantic equivalence or the Version4 workload. Those remain unexecuted and
must not be reported as passing.

No before/after throughput comparison is recorded here. A valid benchmark must
use the same source snapshot and independent output roots for DOP 1, 2, and 12,
with DOP 16 treated only as an admission-limit stress case.
