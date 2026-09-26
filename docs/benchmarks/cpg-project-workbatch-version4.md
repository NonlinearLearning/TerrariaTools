# CPG Project WorkBatch Version4 Acceptance

Status: not accepted.

The Version4 DOP 1 run recorded in the existing handoff stopped before a
complete `manifest.json` was produced. The new project pool implementation has
not been run against the 930-document Version4 snapshot because the user
explicitly requested no tests or acceptance runs in this execution.

The following evidence is therefore still required before this page can claim
acceptance:

- completed and failed document counts for DOP 1, DOP 2, and DOP 12;
- complete manifest and per-file payload signatures;
- peak working set, GC, queued bytes, fragment bytes, and catalog size;
- fixed worker task count and active-worker/idle-reason telemetry;
- cancellation, worker failure, reducer failure, catalog failure, and resume
  cleanup behavior;
- semantic equivalence across graph, query, rule, rewrite, diff, catalog, and
  manifest outputs.

Until those runs are performed, the project pool remains an implementation
under verification rather than a proven Version4 performance improvement.
