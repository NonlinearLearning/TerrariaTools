# NL Test Design

## Status and scope

This document is the test-design entry point for the Roslyn deletion and CPG
pipeline. It replaces the earlier directory I/O and log-only design. The
multi-project move remains an execution prerequisite in
[`2026-07-19-multi-project-test-framework-execution-plan.md`](2026-07-19-multi-project-test-framework-execution-plan.md).
The directory I/O implementation detail remains in
[`2026-07-23-directory-io-log-performance-tests-implementation-plan.md`](2026-07-23-directory-io-log-performance-tests-implementation-plan.md).

The design covers graph construction, rule execution, rewriting, shard
persistence, the CLI host, and performance evidence. It does not make the
deletion prototype a production-safe code removal tool.

## Quality model

The primary quality claim is semantic equivalence. A change is correct only
when each applicable comparison preserves all four observable contracts:

1. Frozen graph identity and the required node and edge sets.
2. Mark and propagated-mark results.
3. Decision results and diagnostics.
4. Rewritten source and diff output.

The comparison axes are serial versus parallel execution, persistence before
and after recovery, and Strict versus Throughput durability. Wall-clock time
and allocations are evidence for performance decisions; they are not ordinary
unit-test pass/fail conditions.

## Test topology

The target layout has one non-test asset library and four test projects. Test
projects may reference production projects and the asset library, but never
another test project.

```text
RoslynDeletionPrototype.Testing
  - TestCodeSet: C# inputs, multi-file fixtures, rule input and fixture metadata
  - TestInfrastructure: isolated workspace, artifact root, canonical comparers

RoslynDeletionPrototype.UnitTests
  - deterministic helpers, graph model, rules, decision conflict collapse

RoslynDeletionPrototype.ContractTests
  - graph build, slice query, DOP equivalence, shard/catalog persistence

RoslynDeletionPrototype.HostTests
  - CLI options, directory traversal, logging, rewrite-plan replay, file output

RoslynDeletionPrototype.PerformanceTests
  - functional behavior across performance options; diagnostic timing only

tools/CpgPersistenceBenchmark and scripts/Run-PerformanceSuite.ps1
  - isolated microbenchmark and real-source measurement evidence
```

`TestCodeSet` contains inputs only. Expected graph, Mark, Decision, rewrite,
and performance results belong to the test that owns the contract. This keeps
fixture changes from silently redefining correctness.

## Required test methods

### Exact contract tests

Use small, named fixtures to assert exact node IDs, edge kinds, graph snapshot
versions, marks, decisions, diagnostics, rewritten text, and diff documents.
These tests own the public semantic examples for syntax, CFG, DataFlow,
logical reduction, declaration-host handling, and parameter shrink behavior.

Every previously fixed defect gets a focused named regression. Unsupported or
ambiguous delete shapes must assert a diagnostic and no unsafe rewrite.

### Differential equivalence tests

Use the serial in-memory path as the baseline. For each fixture, compare it
with every enabled alternative path:

```text
serial in-memory
parallel in-memory
persisted build
recovered persisted build
```

The comparison must include the four observable contracts in the quality
model. A matching hash alone is insufficient for fixtures that are intended to
lock a particular node or edge shape.

### Combinatorial configuration tests

After the project split has a frozen baseline, introduce
`Xunit.Combinatorial` for finite configuration matrices. The initial matrix is:

```text
fixture x DOP (1, 4, 8, 16) x persistence (off, on)
x durability (Strict, Throughput) x file-write concurrency
```

The selected rows must include every durability mode and DOP boundary. Add a
full Cartesian matrix only where a defect proves that pairwise coverage is too
weak. Do not convert these cases to ad hoc `[InlineData]` blocks scattered
across unrelated test classes.

### Property tests

After the finite matrix is stable, introduce `FsCheck` for constrained random
C# fixtures. Generation starts with forms already modeled by the deletion
rules: logical operators, member access, method parameters, named and optional
arguments, delegates, lambdas, indexers, and multi-file references.

For each generated fixture, compare serial, configured DOP, and recovered
persistence outputs. Failure artifacts must contain the effective seed, source
files, execution options, canonical graph summary, rule result, and diff. Do
not claim that whitespace-preserving source transformations retain a snapshot
when a snapshot intentionally contains source spans.

### Persistence state and failure tests

Create a `CpgPersistenceTestKit` before adding broader failure coverage. It
must control writer blocking, write failure, cancellation, catalog commit, and
store-lock timing without depending on real scheduler timing.

Required states:

```text
staging readable / completed catalog invisible
completed catalog readable
write failure cleans temporary state
cancellation leaves no completed catalog entry
lock timeout is cancellable and leaves a reusable store
```

Each state test owns its private temporary root. Tests must not share a global
`Build` or `TestResults` directory.

### Host and directory tests

Host tests use materialized multi-file fixtures and assert CLI-visible output:
options, analyzed-file count, logs, diagnostics, diffs, and rewrite-plan replay.
They do not assert internal helper calls.

The retained directory I/O and logging contract uses one fixed source root with
32 consumer files and one `PlayerInput` file. It runs DOP 1 and DOP 16 with and
without text logs, asserts equivalent decisions, edits, rewritten sources,
complete file records, and successful runtime completion, and writes elapsed
time only through `ITestOutputHelper`.

### Snapshot tests

`Verify` is a later, narrow addition. It is limited to reviewable canonical
representations of complex rewrite plans, graph fragments, and catalog
manifests. Canonicalization sorts collections and removes temporary paths,
timestamps, and elapsed-time fields. A snapshot update is a code review event;
it never replaces exact semantic assertions.

### Performance tests

`PerformanceTests` asserts functional equivalence across performance options
and may log timing. It has no machine-dependent millisecond threshold.

`BenchmarkDotNet` is a later separate benchmark project for in-process
operations such as shard export, serialization, catalog batch writes, and slice
querying. It does not replace the existing persistence benchmark tool.

Whole-directory Terraria runs stay outside `dotnet test`. The runner uses an
explicit source root, isolated logs, warm-ups, at least three measured samples,
and a median report containing source fingerprint, git SHA, SDK, DOP, elapsed
time, peak memory, GC, ThreadPool, and graph size.

### Mutation and concurrency testing

Run `Stryker.NET` only after the rule and contract suites are stable. Start
with `src/Rules` and the decision/rewrite conflict paths in a scheduled job.
Coverage is a diagnostic signal; a coverage percentage is not an acceptance
criterion.

Evaluate `Microsoft Coyote` through a small isolated proof of concept for the
bounded scheduler, shard writer lock, and cancellation seams. Keep it only if
it finds or reproducibly exercises schedules that ordinary controlled tests do
not cover.

## Framework decisions

| Tool | Decision | Purpose and boundary |
| --- | --- | --- |
| xUnit | Keep | Main runner and existing test API. No NUnit or TUnit migration. |
| Xunit.Combinatorial | Add after split | Finite DOP/persistence configuration matrices. |
| FsCheck | Add after matrix baseline | Reproducible constrained random equivalence tests. |
| Verify | Add after canonicalizer | Small reviewed output snapshots. |
| BenchmarkDotNet | Add as a separate project | In-process microbenchmarks only. |
| Coyote | Proof of concept | Scheduler, lock, and cancellation schedules only. |
| Stryker.NET | Scheduled job | Mutation evidence for rules and rewrite decisions. |
| Testcontainers | Do not add now | In-process SQLite and private directories model current storage needs. |
| Generic mocking framework | Do not add by default | Prefer typed fakes in TestKit for filesystem and persistence behavior. |

No framework package is introduced as part of the project split. Each addition
requires its own baseline, package decision, focused tests, and verification.

## Execution tiers and evidence

| Tier | Content | Trigger |
| --- | --- | --- |
| Fast | Unit tests and focused contract tests | Every local code change and pull request |
| Contract | Full graph, DOP, persistence, and rewrite equivalence suite | Relevant pipeline change |
| Host | CLI, directory, logging, and replay tests | Host or option change |
| Performance | Functional performance-option suite | Performance-path change |
| Benchmark | BenchmarkDotNet and persistence benchmark reports | Deliberate performance decision |
| Real source | Warmed Terraria median runs | Default DOP or memory/performance acceptance |
| Scheduled quality | Mutation and optional concurrency exploration | Nightly or explicit review |

Every tier writes to a unique run root under `Build/TestResults/<run-id>/`.
The evidence record contains command, git SHA, SDK, project, start/end time,
exit code, and generated artifacts. A failure artifact must be sufficient to
rerun the failing input without reconstructing temporary state manually.

## Acceptance rules

- A graph, rule, rewrite, or persistence change must run the affected focused
  suite and the affected tier before completion.
- A DOP or persistence change must preserve the four observable contracts
  across the relevant matrix rows.
- A performance default remains unchanged until warmed real-source medians and
  semantic equivalence both pass.
- A test count or test-project move must preserve the frozen baseline outcome.
- Snapshot approval, coverage growth, and faster wall-clock time do not close a
  semantic regression.

## Deferred work

The first implementation increment is the existing project split and tiered
runner. The next increment introduces the canonical equivalence comparator and
the `Xunit.Combinatorial` DOP/persistence matrix. FsCheck, Verify,
BenchmarkDotNet, Coyote, and Stryker.NET follow only after those foundations
are verified.
