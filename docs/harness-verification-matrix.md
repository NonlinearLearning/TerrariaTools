# Performance Harness Verification Matrix

This matrix is the executable contract for the fixed performance-summary
harness. It uses the two independent concurrency settings exposed by the test
configuration helper:

| Case | Directory DOP | CPG DOP | Expected sample sequence |
| --- | ---: | ---: | --- |
| `dop-1-1` | 1 | 1 | one warmup, then three measurements |
| `dop-1-12` | 1 | 12 | one warmup, then three measurements |
| `dop-12-1` | 12 | 1 | one warmup, then three measurements |
| `dop-12-12` | 12 | 12 | one warmup, then three measurements |

Run the focused harness with:

```powershell
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~PerformanceSummaryHarnessTests"
```

The test uses a source fixture created below the process temporary directory.
It does not enable diff, runtime-log, evidence, rewrite-plan or performance
artifact output in the command host. Aggregated summaries are written only to
temporary artifact roots, parsed immediately, and removed by the test cleanup.
The fixture's source paths and contents are checked after all runs so a
measurement cannot modify tracked repository files.

Each runnable case must produce four terminal reports. The first report is
marked `isWarmup`; the remaining three are benchmark measurements. Reports are
compared only after their input, rule, capability, cache, environment, DOP and
business snapshot identities are fixed. `PerformanceEquivalenceChecker` marks
the reports eligible, and `PerformanceSampleAggregator` excludes the warmup
from formal statistics while retaining raw measurements and rejected reports.

The summary DTO is serialized with `PerformanceSummaryDocument` and must keep
the following distinctions:

- `wallElapsedMs` is the end-to-end value; `accumulatedElapsedMs` is separate
  concurrent work and may be null when no pool operation was observed.
- Fewer than three eligible measurements are low confidence and keep raw
  values without formal median/p95 fields.
- An unsupported or invalid DOP is represented as `Skipped` metadata without a
  zero-valued measurement. The fixed matrix uses positive DOP values supported
  by the bounded pool; the focused test also exercises the explicit skipped
  path.
- Normal performance tests do not require `dotnet-trace`,
  `dotnet-counters`, or `dotnet-gcdump`.

For the complete project-level check, run the same project without a filter:

```powershell
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj `
  --no-restore -p:UseSharedCompilation=false
```
