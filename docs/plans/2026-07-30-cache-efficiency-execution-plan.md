# Cache Efficiency Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reduce duplicate cache construction and shard reads, expose cache evidence, and preserve CPG query, persistence, cancellation, and invalidation contracts.

**Architecture:** `NL.Caching` keeps BCL-only reusable mechanisms. `AnalysisRuntime`, `CpgShardQueryResolver`, and `SqliteCpgShardCatalog` keep their keys, freshness, lifecycle, and persistence-integrity rules. First establish regression coverage and measurements; each optimization then changes one cache owner at a time.

**Tech Stack:** .NET 10 preview, C#, xUnit, `ConcurrentDictionary`, `ConditionalWeakTable`, `Lazy<T>`, SQLite CPG catalog.

---

## Scope and Constraints

- Execute in a dedicated worktree. The current worktree has concurrent Rule DAG changes.
- Keep build and test commands sequential because projects share the `Build` output tree.
- Do not add a cross-module cache singleton, default TTL, global byte budget, or domain types to `NL.Caching`.
- Preserve the current shard key: it already includes project, path, source hash, fragment identity, schema version, and profile hash in `CpgShardStore.CreateShardId`.
- Preserve `AnalysisRuntime.InvalidateCaches` and `NextEpoch` semantics. They isolate structure-view cache scopes while reusing compilation-bound weak caches.
- Treat wall-clock timings as diagnostic. Acceptance requires equivalent outputs and reduced attributable work, then compares warmed samples.

## Completion Criteria

1. Cache architecture tests locate `src/NLISSN.Infrastructure/Caching` and prove the project remains BCL-only.
2. Concurrent requests for one weak key and cache type execute the construction factory once; a failed construction can be retried.
3. Concurrent shard cache misses for one location perform one physical `ICpgShardStore.ReadAsync`; cancellation of one waiter does not cancel the shared read; a failed read is retryable.
4. The byte-budget cache provides a consistent snapshot of hits, misses, inserts, replacements, evictions, rejected entries, retained bytes, and count.
5. Existing shard LRU, zero-budget, oversized-entry, persistence integrity, and lookup-order contracts remain green.
6. A same-fixture cold/warmed measurement records cache statistics, physical read count, elapsed time, allocated bytes, and retained bytes before and after each accepted optimization.

## Task 1: Restore the Cache Architecture Guard

**Files:**
- Modify: `tests/NLISSN.ContractTests/Architecture/CacheInfrastructureBoundaryTests.cs:11-20`
- Test: `tests/NLISSN.ContractTests/Architecture/CacheInfrastructureBoundaryTests.cs`

**Step 1: Repair the stale project and source-directory paths**

Replace both `"src", "NL.Caching"` path segments with `"src", "NLISSN.Infrastructure", "Caching"`. Keep the assertions for no package/project references and no `Microsoft.CodeAnalysis`, `NLCPG`, or `NLISSN` source dependency.

**Step 2: Run the focused contract test**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~CacheInfrastructureBoundaryTests
```

Expected: `2/2` passing. The prior failure was `DirectoryNotFoundException` for `src/NL.Caching`.

**Step 3: Commit**

```powershell
git add tests/NLISSN.ContractTests/Architecture/CacheInfrastructureBoundaryTests.cs
git commit -m "test: restore cache infrastructure boundary guard"
```

## Task 2: Make Weak Cache Construction Single-Flight

**Files:**
- Modify: `src/NLISSN.Infrastructure/Caching/WeakTypedCacheRegistry.cs:9-21`
- Modify: `tests/NLISSN.UnitTests/Caching/WeakTypedCacheRegistryTests.cs`

**Step 1: Add public-behavior tests**

Add these tests to `WeakTypedCacheRegistryTests`:

```csharp
[Fact]
public async Task GetOrCreate_ConcurrentSameKeyAndType_InvokesFactoryOnce()
{
    var registry = new WeakTypedCacheRegistry<object>();
    var key = new object();
    var start = new ManualResetEventSlim(false);
    var calls = 0;

    var requests = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
    {
        start.Wait();
        return registry.GetOrCreate(key, _ =>
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(100);
            return new CacheA("value");
        });
    })).ToArray();

    start.Set();
    var values = await Task.WhenAll(requests);

    Assert.Equal(1, Volatile.Read(ref calls));
    Assert.All(values, value => Assert.Same(values[0], value));
}

[Fact]
public void GetOrCreate_FactoryFails_SecondRequestCanRetry()
{
    var registry = new WeakTypedCacheRegistry<object>();
    var key = new object();

    Assert.Throws<InvalidOperationException>(() => registry.GetOrCreate<CacheA>(
        key,
        _ => throw new InvalidOperationException("expected")));

    var value = registry.GetOrCreate(key, _ => new CacheA("retry"));

    Assert.Equal("retry", value.Value);
}
```

The first test must fail against the direct `ConcurrentDictionary<Type, object>.GetOrAdd` value factory when the factory is held open. The second locks the desired retry contract before changing failure handling.

**Step 2: Run the failing tests**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~WeakTypedCacheRegistryTests
```

Expected before implementation: the concurrent construction test fails because the factory executes more than once.

**Step 3: Change registry storage to lazy values**

Change the per-key dictionary to `ConcurrentDictionary<Type, Lazy<object>>`. Wrap the supplied factory in a local `Lazy<object>` using `LazyThreadSafetyMode.ExecutionAndPublication`, insert that lazy value through `GetOrAdd`, then return its `Value` cast to `TValue`.

On a factory exception, conditionally remove the exact installed `Lazy<object>` before rethrowing. Use conditional key/value removal so a later successful request is never removed. Do not cache the exception permanently.

The public interface remains:

```csharp
public TValue GetOrCreate<TValue>(TKey key, Func<TKey, TValue> factory)
    where TValue : class;
```

**Step 4: Run the focused tests**

Run the same UnitTests command. Expected: all `WeakTypedCacheRegistryTests` pass.

**Step 5: Commit**

```powershell
git add src/NLISSN.Infrastructure/Caching/WeakTypedCacheRegistry.cs tests/NLISSN.UnitTests/Caching/WeakTypedCacheRegistryTests.cs
git commit -m "perf: coalesce weak cache construction"
```

## Task 3: Add Byte-Budget Cache Measurements

**Files:**
- Create: `src/NLISSN.Infrastructure/Caching/CacheStatistics.cs`
- Modify: `src/NLISSN.Infrastructure/Caching/ByteBudgetLruCache.cs`
- Modify: `tests/NLISSN.UnitTests/Caching/ByteBudgetLruCacheTests.cs`
- Modify: `src/NLCPG/Analysis/CpgShardQueryResolver.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGSliceQueryTests.cs`

**Step 1: Add a statistics snapshot test**

Add a test that performs one miss, one successful insert, one hit, one replacement, an insertion that evicts an LRU item, and an oversized rejection. Assert the snapshot's event counters and derived `RetainedBytes` and `Count`.

**Step 2: Run the focused test and confirm it fails**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~ByteBudgetLruCacheTests
```

Expected: compile failure until the snapshot type and accessor exist.

**Step 3: Add an immutable snapshot with no domain references**

Create a BCL-only record similar to:

```csharp
public sealed record CacheStatistics(
    long Hits,
    long Misses,
    long Inserts,
    long Replacements,
    long Evictions,
    long RejectedEntries,
    long RetainedBytes,
    int Count);
```

Add `GetStatistics()` to `ByteBudgetLruCache`. Update counters under its existing `_gate`; this produces a coherent snapshot and adds no new synchronization. Count an entry as rejected when its weight is zero or exceeds the configured budget. Count every removed resident entry as an eviction, including replacement only when removal is capacity-driven; count replacement separately.

Expose the snapshot from `CpgShardQueryResolver` as a read-only diagnostic property. Do not add rule, CPG, or logging types to `NL.Caching`.

**Step 4: Extend the resolver contract test**

Use the existing persisted two-shard LRU fixture in `NLCPGSliceQueryTests`. Assert cache hit and eviction counters along with its existing file-deletion behavior, proving a retained entry serves after its file is deleted and an evicted entry requires a read.

**Step 5: Run focused tests**

Run sequentially:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~ByteBudgetLruCacheTests
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~NLCPGSliceQueryTests
```

Expected: existing zero-budget, oversized-entry, and LRU tests remain green; new counters match observable cache behavior.

**Step 6: Commit**

```powershell
git add src/NLISSN.Infrastructure/Caching/CacheStatistics.cs src/NLISSN.Infrastructure/Caching/ByteBudgetLruCache.cs src/NLCPG/Analysis/CpgShardQueryResolver.cs tests/NLISSN.UnitTests/Caching/ByteBudgetLruCacheTests.cs tests/NLISSN.ContractTests/Cpg/NLCPGSliceQueryTests.cs
git commit -m "feat: expose byte budget cache statistics"
```

## Task 4: Coalesce Concurrent Shard Cache Misses

**Files:**
- Modify: `src/NLCPG/Analysis/CpgShardQueryResolver.cs:10-63`
- Create: `tests/NLISSN.ContractTests/Cpg/CpgShardQueryResolverTests.cs`

**Step 1: Add a read-through concurrency contract test**

Create a test-local `ICpgShardCatalog` returning one fixed `CpgShardLocation` and a blocking `ICpgShardStore` that records `ReadAsync` invocations. Start 16 `FindByNodeAsync` callers, release the first physical read only after all callers are waiting, then assert:

```csharp
Assert.Equal(1, store.ReadCount);
Assert.All(results, result => Assert.Single(result));
```

Add two separate tests:

- cancel one waiting caller and assert another caller completes from the same read;
- make the shared read fail, then retry and assert a second physical read succeeds.

**Step 2: Run the new test and confirm duplicate reads**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~CpgShardQueryResolverTests
```

Expected before implementation: the duplicate-read test reports more than one `ReadAsync` call.

**Step 3: Add resolver-local in-flight coordination**

Add an in-flight dictionary keyed by `ShardId`. On an LRU miss, atomically create or join one shared load task. The shared task reads using the resolver's lifetime token or `CancellationToken.None`; every request waits using its own `cancellationToken` through `Task.WaitAsync`.

On successful load, write the shard to `ByteBudgetLruCache` with `location.ByteLength`, return the shard, and remove the in-flight task. On failure, remove the matching task before rethrowing. Keep this coordinator local to `CpgShardQueryResolver`: the routing-index cache retains successful validation tasks, whereas this table represents only in-progress I/O and has a different lifecycle.

**Step 4: Run focused and surrounding CPG tests**

Run sequentially:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~CpgShardQueryResolverTests
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~NLCPGSliceQueryTests
```

Expected: one physical read per concurrent miss, correct waiter-local cancellation, retry after a failed load, and unchanged slice results.

**Step 5: Commit**

```powershell
git add src/NLCPG/Analysis/CpgShardQueryResolver.cs tests/NLISSN.ContractTests/Cpg/CpgShardQueryResolverTests.cs
git commit -m "perf: coalesce concurrent shard cache misses"
```

## Task 5: Measure Resident Weight and Lock Contention

**Files:**
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- Modify only if justified: `src/NLCPG/Analysis/CpgShardQueryResolver.cs`
- Modify only if justified: `src/NLISSN.Infrastructure/Caching/ByteBudgetLruCache.cs`

**Step 1: Add deterministic measurement coverage**

Use a fixed persisted-shard fixture to record cold and warmed runs. Capture:

- `CacheStatistics` snapshot;
- physical shard reads;
- allocation delta using `GC.GetAllocatedBytesForCurrentThread` where the measurement remains single-threaded;
- elapsed time as diagnostic output;
- serialized `ByteLength` and process retained-memory delta as separate values.

Do not assert a machine-dependent elapsed-time threshold. Assert that the warmed run performs fewer physical reads and preserves the exact ordered shard IDs and slice result.

**Step 2: Run the performance project**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~Cache
```

Expected: deterministic semantic assertions pass and the output contains cold/warmed cache evidence.

**Step 3: Decide from evidence**

- If deserialized shard retention materially exceeds serialized `ByteLength`, define a resolver-owned `CpgFrozenShard` weight estimator and pass its result to the generic LRU. Keep the estimator out of `NL.Caching`.
- If lock wait or throughput measurement proves contention, evaluate approximate recency promotion or a sharded cache. Preserve exact LRU until the benchmark demonstrates that contention dominates shard read time.
- If hit rate stays low, tune the resolver's per-instance budget at composition rather than introducing a global budget.

**Step 4: Commit only an evidence-backed change**

```powershell
git add tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs src/NLCPG/Analysis/CpgShardQueryResolver.cs src/NLISSN.Infrastructure/Caching/ByteBudgetLruCache.cs
git commit -m "perf: tune shard cache from warmed measurements"
```

Omit unchanged files from the commit. Do not make this commit if the measurement does not identify an attributable benefit.

## Task 6: Gate Routing-Index Candidate Streaming on Contract Approval

**Files:**
- Investigate: `src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs:1010-1110`
- Test: `tests/NLISSN.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`
- Potentially modify: `src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs`

`ReadRoutingIndexCandidatesAsync` currently materializes and validates every completed build before node, symbol, or span lookup examines the newest candidate. This is the largest cold-query cost as completed-build history grows.

**Step 1: Capture baseline evidence**

Create a multi-build fixture with a newest matching build and older completed builds. Record routing-index file reads, deserializations, and result locations for node, symbol, and span lookups.

**Step 2: Decide the integrity contract before editing**

Choose one explicitly:

1. Query-time validation applies only to candidates examined before finding a result. Then replace materialization with an ordered async stream and stop on the first matching validated build.
2. Every completed build must be validated before every lookup returns. Retain eager validation and move cost reduction to build publication, retention, or an independently verified manifest index.

Current tests prove a changed manifest is rejected, but they do not establish a requirement to validate unrelated older builds before returning a newer match.

**Step 3: Implement only after the choice is recorded**

Add tests covering newest-build precedence, legacy fallback, a corrupt older manifest, cancellation, and unchanged shard location ordering. Preserve the sidecar's completed-build visibility contract.

**Step 4: Run targeted persistence tests**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~SqliteCpgShardCatalogTests
```

Expected: results and integrity behavior follow the approved contract; cold-query work is recorded separately from warmed-cache work.

## Final Verification and Documentation

Run sequentially after each accepted commit:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet build .\src\NLISSN.Infrastructure\Caching\NL.Caching.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ByteBudgetLruCacheTests|FullyQualifiedName~WeakTypedCacheRegistryTests"
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CacheInfrastructureBoundaryTests|FullyQualifiedName~CpgShardQueryResolverTests|FullyQualifiedName~NLCPGSliceQueryTests|FullyQualifiedName~SqliteCpgShardCatalogTests"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

At execution time, update `feature_list.json` with the selected scope and definition of done, then keep `progress.md` limited to the current cache phase, verified commands, remaining gate, and measurement status.

## References

- `docs/plans/2026-07-30-cache-infrastructure-policy-boundary-research.md`
- `src/NLISSN.Infrastructure/Caching/ByteBudgetLruCache.cs`
- `src/NLISSN.Infrastructure/Caching/WeakTypedCacheRegistry.cs`
- `src/NLCPG/Analysis/CpgShardQueryResolver.cs`
- `src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
