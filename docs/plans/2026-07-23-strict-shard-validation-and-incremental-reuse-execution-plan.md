# Strict Shard Validation and Incremental Reuse Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Preserve Strict durability while making shard validation linear in shard size, avoiding materialization during validation, and reusing provably unchanged method shards across an edited file.

**Architecture:** Strict writes must validate bytes read back from the flushed temporary file before publication. A streaming structural validator will consume that file directly, parse the binary format without creating `CpgFrozen*` objects or decoded strings, and use an `ArrayPool`-rented local-index bitmap for O(N + E) endpoint checks. Reuse is catalog-backed and opt-in per fragment: a new session references an existing completed shard only when its reusable key, NodeIds, schema, and builder profile prove compatibility; skeleton and boundary shards are rebuilt for every changed file.

**Tech Stack:** .NET 10, `FileStream`, `System.IO.Hashing` or `IncrementalHash`, SQLite via `Microsoft.Data.Sqlite`, xUnit, `tools/CpgPersistenceBenchmark`.

---

## Scope And Invariants

- `Strict` means: write temporary shard bytes, flush the writer stream with `flushToDisk: true`, read the same temporary file back, verify its SHA-256 and complete binary structure, then atomically move it into the completed session.
- `Throughput` keeps its current header-only per-shard verification and session-finalization behavior. This work must not silently make either durability mode weaker.
- Structural validation must reject the same malformed inputs as the current `Deserialize(...)` path: unsupported magic/version, missing required strings, malformed section lengths, trailing bytes, invalid role/adjacency combinations, and orphan local edges.
- Validation must be O(N + E + B + S), where N is nodes, E is local edges, B is boundary edges, and S is symbol locations. Its steady-state path must not allocate arrays, strings, records, or collections proportional to shard contents: it rents bounded buffers and the local-index bitmap from `ArrayPool`, then returns them in `finally`. It must not construct `CpgFrozenNode[]`, `CpgFrozenEdge[]`, `CpgFrozenBoundaryEdge[]`, or `CpgSymbolLocation[]`.
- Reuse must never make an incomplete/invalid session visible. It may only reference shard bytes from a completed build session.
- A reuse hit must preserve frozen graph snapshot, NodeIds, edges, shard-backed slice results, rule output, and rewrite output for DOP 1, 8, 12, 14, and 16.
- Default `MaxConcurrentShardFileWrites` remains `2`. The 2026-07-23 synthetic result is evidence for further investigation, not a basis for a default change.

## Baseline Evidence

- In [CpgShardStore.cs](../../src/MinimalRoslynCpg/Persistence/CpgShardStore.cs), Strict currently calls `Validate(payload, ...)` on the in-memory payload after writing the file. It does not read the temporary shard back.
- `Deserialize(...)` checks each local edge using two `nodes.Any(...)` scans, producing O(E * N) work at [CpgShardStore.cs](../../src/MinimalRoslynCpg/Persistence/CpgShardStore.cs:287).
- The 96-method Strict DOP 12 benchmark reported validation as the dominant phase: six-sample medians were `4409 ms` with one file writer, `5704 ms` with two, `5691 ms` with four, and `5352 ms` with six. Serialization and flush were negligible in that synthetic fixture.
- Existing shard lookup keys include the whole-file source hash. A changed file therefore cannot reuse an unchanged method fragment through `TryAcquireAsync`; reuse needs a separate, narrower catalog key.

## Task 1: Lock Down Strict Read-Back Semantics

**Files:**

- Modify: `src/MinimalRoslynCpg/Persistence/CpgShardStore.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/Cpg/CpgShardContractTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/Cpg/CpgShardBuildCoordinatorTests.cs`

**Step 1: Write failing Strict write-path tests**

Add a store seam that can mutate the `.tmp` file after write and before validation. Cover:

```csharp
[Fact]
public async Task WriteAsync_Strict_WhenReadBackBytesAreMutated_ThrowsAndDoesNotPublishShard()
{
    // Arrange a Strict store with the controlled post-write mutation seam.
    // Act: write a valid shard.
    // Assert: InvalidDataException, no .cpgbin, no .tmp.
}

[Fact]
public async Task WriteAsync_Throughput_WhenReadBackBytesAreMutated_DefersFullValidationToFinalization()
{
    // Preserve current Throughput contract: per-shard header validation is not full read-back validation.
}
```

**Step 2: Run the focused tests and verify RED**

Run:

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardContractTests|FullyQualifiedName~CpgShardBuildCoordinatorTests"
```

Expected: the Strict mutation test fails because validation still consumes the original in-memory `payload`.

**Step 3: Make Strict flush and validate the actual temporary file**

- Replace `File.WriteAllBytesAsync(...)` plus the later read-only `Flush(true)` call with one writable `FileStream`.
- Write the serialized payload, call `Flush(true)` on that writer stream, then reopen/read the `.tmp` file.
- Compute SHA-256 from read-back bytes and require equality with the pre-write hash.
- Stream the read-back temporary file through hashing and structural validation before `File.Move(...)`; do not call `File.ReadAllBytesAsync` or retain a second full payload.
- Keep temporary-file deletion in the existing `finally` block and do not alter the completed-session visibility transition.

**Step 4: Run the focused tests and verify GREEN**

Run the command from Step 2. Expected: all shard contract and coordinator tests pass.

**Step 5: Commit**

```powershell
git add src/MinimalRoslynCpg/Persistence/CpgShardStore.cs tests/RoslynDeletionPrototype.Tests/Cpg/CpgShardContractTests.cs tests/RoslynDeletionPrototype.Tests/Cpg/CpgShardBuildCoordinatorTests.cs
git commit -m "Verify Strict shards from flushed temporary bytes"
```

## Task 2: Replace Materializing O(E*N) Validation

**Files:**

- Modify: `src/MinimalRoslynCpg/Persistence/CpgShardStore.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/Cpg/CpgShardContractTests.cs`

**Step 1: Write failing structural-validator tests**

Create malformed binary fixtures from an otherwise valid serialized shard. Cover duplicate/missing local indexes, source or target local indexes with no node, truncated section content, and trailing data. Add a large synthetic shard whose local edges reference every node so the test can assert a linear validation counter or bounded endpoint-probe count without using timing as the primary assertion. Add an allocation regression that warms the validator, validates the same shard repeatedly, and bounds per-run allocated bytes; test hooks may expose parser counters but production code must not expose mutable validation state.

**Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardContractTests"
```

Expected: new validator-specific tests fail because no non-materializing validator exists.

**Step 3: Implement an allocation-free streaming `ValidatePayload` path**

- Introduce an internal validator that advances through a bounded rented byte buffer over the temporary-file `FileStream`, with explicit bounds checks and a single pass that also feeds `IncrementalHash`. Do not materialize the file payload or create a `ReadOnlySequence` segment per read.
- Register local indexes in an `ArrayPool`-rented bitmap or stamped `int[]`; use direct indexed membership for every edge endpoint. Reject duplicate indexes and return every rented array in `finally`. Do not use `Enumerable.Any`, `HashSet<int>`, LINQ, or a scan through node records on the edge path.
- Parse BinaryWriter's 7-bit UTF-8 string prefix directly. Validate UTF-8 and required-string whitespace with `Rune` decoding while discarding content; optional strings are skipped without constructing `string` instances. Preserve integer widths, field order, magic bytes, format version, role, adjacency, boundary-edge, and symbol-location validation.
- Keep the validator's working-set cap explicit. A malformed count, length, or local index that would exceed the configured cap must fail with `InvalidDataException`, avoiding unbounded pooled-rent requests from corrupt input.
- Leave `Deserialize(...)` as the read/query materialization path. Strict write validation must call the new validator instead of `Deserialize(...)`.
- Add telemetry fields only if needed to distinguish `ReadBackMilliseconds`, `HashMilliseconds`, and `StructuralValidationMilliseconds`; keep existing public telemetry fields compatible.

**Step 4: Run focused tests and the CPG persistence suite**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShard|FullyQualifiedName~SqliteCpgShardCatalog|FullyQualifiedName~RoslynCpgSliceQuery|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgNodeIdContractTests"
```

Expected: no malformed shard is accepted; DOP and shard-backed query regressions remain green.

**Step 5: Commit**

```powershell
git add src/MinimalRoslynCpg/Persistence/CpgShardStore.cs tests/RoslynDeletionPrototype.Tests/Cpg/CpgShardContractTests.cs
git commit -m "Validate Strict shard structure without materializing graphs"
```

## Task 3: Add Completed-Session Fragment Reuse Metadata

**Files:**

- Modify: `src/MinimalRoslynCpg/Persistence/CpgShardContracts.cs`
- Modify: `src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardSchema.cs`
- Modify: `src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- Modify: `src/MinimalRoslynCpg/Persistence/Sqlite/CpgCatalogBatchWriter.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/Cpg/SqliteCpgShardCatalogTests.cs`

**Step 1: Write failing catalog tests**

Define an internal `CpgReusableFragmentKey` containing project id, relative path, schema version, profile hash, fragment kind, span start, span length, fragment hash, and an explicit NodeId-compatibility fingerprint. Test that only completed sessions return a candidate, newer compatible completed sessions win deterministically, and schema/profile/span/fingerprint mismatch returns no candidate.

**Step 2: Run the catalog tests and verify RED**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SqliteCpgShardCatalogTests"
```

Expected: compile or assertion failure because reusable fragment metadata and lookup do not exist.

**Step 3: Add schema migration and catalog API**

- Bump `SqliteCpgShardSchema.Version`; add a completed-session-only reusable-fragment index/table or equivalent indexed query.
- Stage the reusable key with every primary method fragment after its shard is validated.
- Add a catalog method that returns a verified location plus metadata only from a completed build.
- Preserve existing file-source-hash lookup and session visibility behavior. Reuse metadata is supplementary; it must never change normal cache acquisition semantics.

**Step 4: Run catalog tests and verify GREEN**

Run the command from Step 2. Expected: deterministic compatible lookup and no leaked building/invalid session candidate.

**Step 5: Commit**

```powershell
git add src/MinimalRoslynCpg/Persistence/CpgShardContracts.cs src/MinimalRoslynCpg/Persistence/Sqlite tests/RoslynDeletionPrototype.Tests/Cpg/SqliteCpgShardCatalogTests.cs
git commit -m "Index completed CPG fragments for safe reuse"
```

## Task 4: Reuse Unchanged Method Shards During a Changed File Build

**Files:**

- Modify: `src/MinimalRoslynCpg/Builder/CpgShardBuildCoordinator.cs`
- Modify: `src/MinimalRoslynCpg/Builder/CpgShardBuildSession.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Modify: `src/MinimalRoslynCpg/Persistence/CpgFrozenShardExporter.cs` only if it must expose the NodeId-compatibility fingerprint
- Test: `tests/RoslynDeletionPrototype.Tests/Cpg/CpgShardBuildCoordinatorTests.cs`

**Step 1: Write failing changed-file reuse tests**

Use a two-method source where an edit changes only the later method without shifting the first method span. Assert:

- the first method is reused from the old completed session;
- the changed method, file skeleton, and boundary adjacency shard are freshly written;
- the final graph snapshot and shard-backed slice equal a no-cache rebuild;
- the new session becomes visible only after fresh and reused entries are staged and completed.

Add negative cases for changed builder profile, schema version, local span, fragment content, NodeId fingerprint, a corrupt candidate, cancellation, and a candidate from an invalid session.

**Step 2: Run the coordinator tests and verify RED**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShardBuildCoordinatorTests"
```

Expected: the build republishes every method shard because it has no reusable-fragment lookup path.

**Step 3: Implement conservative reuse**

- Build the reusable key only after method-fragment ownership and deterministic NodeId allocation are known.
- Query the catalog before exporting/publishing each method shard. Verify the candidate shard file hash and structural validity before staging it into the new build session.
- Stage a reused location in source order without writing, but keep the catalog dispatcher as the single deterministic committer.
- Always regenerate `file-skeleton` and boundary-adjacency shards for a changed file. Do not reuse an operation shard when its NodeId fingerprint differs, even when its source text hash matches.
- Add telemetry: reuse hit/miss/rejection counts, reused shard bytes, avoided serialization/validation time, and reused physical locations.
- Do not remove old completed sessions or physical shards while a newer session can still reference them. Add explicit reference/liveness accounting before any future cleanup feature.

**Step 4: Run focused equivalence tests**

Run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShard|FullyQualifiedName~SqliteCpgShardCatalog|FullyQualifiedName~RoslynCpgSliceQuery|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgNodeIdContractTests"
```

Expected: unchanged methods avoid writes; all graph/query equivalence checks pass.

**Step 5: Commit**

```powershell
git add src/MinimalRoslynCpg/Builder src/MinimalRoslynCpg/Persistence tests/RoslynDeletionPrototype.Tests/Cpg
git commit -m "Reuse compatible completed CPG method shards"
```

## Task 5: Benchmark And Release Decision

**Files:**

- Modify: `tools/CpgPersistenceBenchmark/Program.cs` only if additional telemetry projection is needed
- Modify: `progress.md`
- Modify: `docs/plans/2026-07-19-streaming-cpg-shard-follow-up.md`

**Step 1: Establish post-change synthetic measurements**

Run the existing matrix and retain a dated JSON report:

```powershell
dotnet run --no-restore --project .\tools\CpgPersistenceBenchmark\CpgPersistenceBenchmark.csproj -- --output .\Build\cpg-persistence-benchmark-<date>-strict-validation-reuse.json
```

Then run the focused writer matrix:

```powershell
dotnet run --no-build --project .\tools\CpgPersistenceBenchmark\CpgPersistenceBenchmark.csproj -- --fixture large-single-file --durability Strict --dop 12 --file-write-concurrency 1,2,4,6 --output .\Build\cpg-persistence-benchmark-<date>-strict-write-concurrency.json
```

**Step 2: Run real-source reuse measurements**

Create a controlled two-run fixture from the same source tree: first build cold, then edit a late method while retaining an earlier method span. Record reuse telemetry, wall-clock, peak managed heap, peak working set, total shard bytes, and graph counts. Repeat three warmed samples.

**Step 3: Run the complete verification set**

Run:

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

Expected: build and all tests pass; harness has no output; no whitespace errors.

**Step 4: Update retained facts**

Record measured validation/read-back/reuse cost in `progress.md` and replace superseded assumptions in the streaming follow-up plan. Keep `Strict` and `MaxConcurrentShardFileWrites = 2` as defaults unless repeated real-source data supports a change.

**Step 5: Commit**

```powershell
git add tools/CpgPersistenceBenchmark progress.md docs/plans
git commit -m "Measure Strict shard validation and reuse performance"
```

## Completion Criteria

- Strict validates bytes actually read back from a flushed temporary file before atomic publication.
- Structural validation stays allocation-free for frozen graph objects and scales linearly with node and edge counts.
- Corrupt, truncated, orphan-edge, invalid-role, and stale temporary shards retain their current recovery behavior.
- Compatible unchanged method fragments reuse physical bytes from completed sessions; changed/skeleton/boundary fragments are rebuilt conservatively.
- Reuse never exposes building/invalid sessions and never invalidates a still-referenced physical shard.
- DOP, durability, graph snapshot, slice, rule, and rewrite equivalence tests pass.
- Reports distinguish read-back, hashing, structural validation, flush, reuse hits/misses, and avoided write cost; real-source measurements justify any default change.

## Execution Record (2026-07-23)

- Strict writes now flush the writable temporary stream and validate the bytes read from
  that temporary file before the atomic move. The validator hashes and structurally
  validates in one stream pass; it does not construct frozen graph records or decoded
  strings. Local node indexes are checked with a bounded pooled bitmap, removing the
  former edge-by-node scan.
- `CpgPersistenceDurabilityMode` is explicit on `CpgPersistenceOptions`: `Strict` is
  the default, while `Throughput` performs only post-write header validation. Focused
  tests prove a temporary-file mutation rejects Strict publication and remains
  detectable on later Throughput read.
- Compatible completed operation fragments are cataloged by project/path/schema/profile,
  span, fragment hash, and NodeId fingerprint. The streaming publisher stages a verified
  physical location on a reuse hit and rebuilds skeleton, boundary, and changed fragments.
  The changed-later-method regression observes a reuse hit and graph-snapshot equivalence.
- `CpgPersistenceTelemetry` and benchmark JSON include reuse hits, misses, rejections,
  reused bytes, read-back, hash, and structural-validation timings. The Strict 96-method
  DOP 12 smoke report is
  `Build/cpg-persistence-benchmark-20260723-strict-validation-reuse-smoke.json`:
  median wall time `3640 ms`, read-back `<1 ms`, SHA-256 `2 ms`, structural validation
  `230 ms`, and flush `367 ms`. It creates a fresh store, so all reuse counters are zero
  by design.
- The warmed repository fixture report is
  `Build/cpg-persistence-benchmark-20260723-repository-dataflow-pass-reuse.json`.
  It builds `DataFlowPass.cs`, changes only a late same-width string literal, and uses
  one warmup plus three samples at Strict/DOP 12. Its medians are cold `20562 ms`,
  incremental `21505 ms`, 47 reused shards, one changed-fragment miss, no rejection,
  and `642808 bytes` reused. Reuse is correct but did not improve end-to-end time on
  this source, so the current defaults remain unchanged.

### Verification And Remaining Work

- `CpgShardStoreLock` now defers disposal after cancellation until a queued registered
  wait callback has completed. This closes a testhost crash where that callback called
  `Semaphore.Release()` after its handle was disposed. The targeted lock suite passed
  `5/5`, including a cancellation/release race regression.
- The Contract CPG shard/catalog/slice/partition/NodeId filter passed `122/122`, and
  the complete split functional suite passed on 2026-07-23: Unit `24/24`, Contract
  `143/143`, and Host `350/350`. The former aggregate test project is now an empty
  compatibility shell after this split; the external Terraria timing workload belongs
  to the separate Performance project and is not a functional-test gate.
- The Strict smoke report and three-sample repository reuse report remain the release
  evidence. The latter reports cold `20562 ms`, incremental `21505 ms`, 47 reused
  shards, one miss, zero rejection, and `642808` reused bytes. No default changes are
  justified. Harness consistency and `git diff --check` also passed.
- All execution-plan completion criteria are now satisfied.
