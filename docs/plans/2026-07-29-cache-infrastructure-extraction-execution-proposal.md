# Cache Infrastructure Extraction Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Extract two BCL-only cache primitives into `src/NL.Caching`—a weak-keyed typed registry and a byte-budget LRU—while keeping cache keys, invalidation, query semantics, and Roslyn/CPG ownership in their current modules.

**Architecture:** `NL.Caching` owns generic in-process data structures only. `NLISSN.Core` continues to decide compilation and analysis cache scope; `NLCPG` continues to decide shard identity, byte weight, read ordering, and persistent-query visibility. No cache becomes process-global and no existing cache is migrated merely because it uses a dictionary.

**Tech Stack:** .NET 10, BCL `ConditionalWeakTable`, `ConcurrentDictionary`, `Dictionary`, `LinkedList`, `lock`, xUnit, existing Unit, Host, and Contract test projects.

---

## GitHub reference study and reverse learning

The following are ten public repositories whose GitHub repository pages were checked on 2026-07-29. Each had at least 50,000 stars at that time. The source links are the design evidence; these projects are neither runtime dependencies nor templates to copy wholesale.

| Repository | GitHub stars at check | Cache design read | Reusable lesson for this repository | Deliberately excluded |
| --- | ---: | --- | --- | --- |
| [Redis](https://github.com/redis/redis/blob/unstable/src/evict.c) | 75.7k | Eviction policy is explicit (`allkeys-lru`, LFU, TTL variants) and its data-structure work is separated from command/storage semantics. | Give the LRU a byte budget and recency contract only; keep shard identity and I/O in `CpgShardQueryResolver`. | Redis policy selection, sampling, background expiry, and process-wide configuration. |
| [Elasticsearch](https://github.com/elastic/elasticsearch/blob/main/server/src/main/java/org/elasticsearch/common/cache/Cache.java) | 77.6k | The cache owns entry retention and eviction accounting while callers retain indexing/query policy. | Keep weighted retention as a small primitive, then prove resolver ordering and persistence visibility at the caller. | Search-segment cache hierarchy and cluster-level memory policy. |
| [Spring Framework](https://github.com/spring-projects/spring-framework/blob/main/spring-context/src/main/java/org/springframework/cache/CacheManager.java) | 60.1k | `CacheManager` is an orchestration boundary, with provider semantics below it. | A reusable primitive may sit below domain policy; `AnalysisRuntime` remains the public compilation-cache seam. | A generic cache-manager/provider abstraction or dependency-injection integration. |
| [Django](https://github.com/django/django/blob/main/django/core/cache/backends/base.py) | 88.2k | Backend cache keys include a prefix and version, making invalidation scope explicit. | Preserve `CacheScopeKey` and rule-specific keys at their owners; cache extraction must not flatten semantic scope. | Key-prefix/version API changes or cross-process invalidation. |
| [React](https://github.com/facebook/react/blob/main/packages/react/src/ReactCacheImpl.js) | 247k | Render cache roots use weak ownership and type-partitioned values. | `ConditionalWeakTable<TKey, ConcurrentDictionary<Type, object>>` matches the needed lifetime and type partition for a compilation registry. | Process-global memoization and retention beyond the compilation lifetime. |
| [webpack](https://github.com/webpack/webpack/blob/main/lib/cache/PackFileCacheStrategy.js) | 65.9k | Persistent packs distinguish serialization, snapshots, and invalidation metadata from in-memory object retention. | Keep persistence/manifests out of the LRU migration; byte-budget LRU only retains already-read `CpgFrozenShard` objects. | Pack-file persistence, snapshot protocol, and serialization format changes. |
| [Vite](https://github.com/vitejs/vite/blob/main/packages/vite/src/node/optimizer/index.ts) | 82.1k | Optimizer metadata binds cache reuse to input and environment hashes. | Do not claim cache correctness from object identity alone; source/config/format validity remains a separate persistence concern. | Dependency optimizer cache, browser hash, and filesystem metadata changes. |
| [TypeScript](https://github.com/microsoft/TypeScript/blob/main/src/compiler/builder.ts) | 110k | Incremental builder state keeps changed-file and diagnostic/emit caches behind program-version boundaries. | Keep `NextEpoch()` and `InvalidateCaches()` behavior stable and test it explicitly during registry migration. | Compiler incremental-state redesign or new cache epochs. |
| [Kubernetes](https://github.com/kubernetes/kubernetes/blob/master/staging/src/k8s.io/client-go/tools/cache/thread_safe_store.go) | 124k | A lock protects the primary store and derived index updates as one coherent mutation. | One private lock must guard LRU dictionary, list, and byte counter together; never execute I/O or callbacks there. | Informer/indexer API, watch invalidation, or shared cache lifecycle. |
| [Go](https://github.com/golang/go/blob/master/src/cmd/go/internal/cache/cache.go) | 135k | Persistent build cache keys are action/input identities and cache content is validated before reuse. | A future durable CPG cache needs an explicit action key and validation contract; in-memory extraction must not impersonate that feature. | Build-cache protocol, on-disk layout, and corruption-recovery behavior. |

Source-reading note: the raw source files for Redis, Django, React, webpack, TypeScript, and Go were downloaded during this review. GitHub's shared REST quota was exhausted while retrieving the remaining four raw files, so their linked primary source entrypoints and the well-bounded conclusions above must be rechecked from source before any design beyond this proposal relies on them. Star counts are repository-page snapshots, not durable release metadata.

### Resulting boundary rules

1. Extract storage mechanics only when their keys and values are domain-neutral; retain cache scope, invalidation, I/O, and persistence validation at the current owner.
2. Treat weak lifetime, type partitioning, byte accounting, recency, and lock scope as independently testable contracts.
3. Keep durable-cache validity separate from in-memory eviction. A cache hit can improve latency; it must never determine semantic correctness.
4. Do not introduce a manager, provider, remote backend, or single-flight API without a caller-owned failure, cancellation, and invalidation contract.

## Scope and non-goals

### Extract in this feature

- `WeakTypedCacheRegistry<TKey>`: a weak-keyed, type-partitioned registry matching the current `AnalysisRuntime.RuntimeCacheRegistry` behaviour.
- `ByteBudgetLruCache<TKey, TValue>`: a bounded in-memory LRU with explicit byte weights and synchronous `TryGet` / `Set` operations.
- The two direct consumers:
  - `AnalysisRuntime` compilation cache registration.
  - `CpgShardQueryResolver` shard-read retention.

### Keep in their owning modules

- `MarkAnalysisSnapshot`: its keys contain Roslyn nodes, mark-region rules, target-name normalization, and slice-query budgets.
- `NLCPGStructureViewBuilder`: its weak graph/run caches depend on graph freezing, fragment order, and `AnalysisRuntime.CacheScopeKey` invalidation.
- `NLCPGSliceQuery`: its instance cache is keyed by graph snapshot and traversal budgets.
- `CpgFrozenShardGraphReader.NodeIndexes`: its weak key is a frozen-shard object and its value is CPG record layout knowledge.
- `NLCPGBuilder` temporary maps, including call-target and CFG maps: they are intentionally cleared after `FreezeQueryIndex()`.
- `ParameterShrinkAnalyzer.CompilationScanCache`: the cache value and all tree-scan facts remain rule-owned; only its registry host moves with `AnalysisRuntime`.

### Explicit non-goals

1. Do not introduce a global `CacheManager`, dependency injection container, cache provider abstraction, Redis, or a package dependency.
2. Do not add asynchronous loading or single-flight behavior to `ByteBudgetLruCache`. `CpgShardQueryResolver` must continue to perform I/O outside its cache lock; a shared async loader requires a separate cancellation and failure contract.
3. Do not change CPG persistence manifests, catalog routing, source hashes, default DOP, or query-result semantics.
4. Do not migrate `MarkAnalysisSnapshot` to a generic map. Its `Lazy(..., ExecutionAndPublication)` instances deliberately coalesce expensive semantic work and are part of its owner contract.

## Source facts and design decisions

| Current owner | Current mechanism | Extraction decision |
| --- | --- | --- |
| `AnalysisRuntime` | `ConditionalWeakTable<Compilation, ConcurrentDictionary<Type, object>>` | Move the data structure only; retain `GetOrCreateCompilationCache` as the Core API. |
| `CpgShardQueryResolver` | `Dictionary` + recency `LinkedList`, bounded by `CpgShardLocation.ByteLength` | Move the LRU mechanics only; retain catalog lookup, read, ordering and shard identity in NLCPG. |
| `MarkAnalysisSnapshot` | concurrent dictionaries of `Lazy<T>` over semantic/graph facts | Keep local. A generic extraction would hide rule-specific key and failure semantics. |
| `NLCPGStructureViewBuilder` | two weak tables plus graph-specific neighbor caching | Keep local. Cache scope must advance with `InvalidateCaches()`. |
| `NLCPGSliceQuery` | graph-version-and-budget query map | Keep local. A result is cacheable only when not truncated. |

The boundaries implement the repository-specific consequences of the reference study above. They retain the present public seams and do not import any reference project's API or dependency.

## Invariants

1. `src/NL.Caching` targets `net10.0`, enables nullable reference types, and has no `PackageReference` or `ProjectReference`.
2. `WeakTypedCacheRegistry<TKey>` accepts only reference-type keys. The registry must not retain a key after its caller releases it.
3. `WeakTypedCacheRegistry<TKey>.GetOrCreate<TValue>` partitions values by both key identity and `TValue` type. Its first implementation preserves the current `ConcurrentDictionary.GetOrAdd` factory semantics; it must not silently add sticky exception caching or single-flight execution.
4. `ByteBudgetLruCache<TKey, TValue>` stores only entries whose positive weight fits within the configured byte budget. `0` budget and oversized entries bypass storage.
5. A successful `TryGet` promotes the entry to MRU. `Set` replaces the same key without leaving a duplicate node or corrupting accounted bytes. Inserting a fitting entry evicts LRU entries until the invariant holds.
6. The LRU protects its own dictionary/list/accounting under one private lock. It must never call storage I/O, user callbacks, or logging while that lock is held.
7. `CpgShardQueryResolver.OpenLocationsAsync` retains current source ordering (`ShardId` ordinal), cancellation propagation, and catalog/store calls. The LRU primitive does not make the resolver a shared single-flight loader.
8. `AnalysisRuntime.InvalidateCaches()` and `NextEpoch()` keep the same compilation registry, as they do today; only `CacheScopeKey` changes for view invalidation.
9. All production cache keys and invalidation policy remain visible at the domain call site. `NL.Caching` must contain no Roslyn, CPG, rule, persistence, or CLI type.

## Phase 0: Record the feature and establish a baseline

### Task 1: Track the extraction independently from the active persistence feature

**Files:**

- Modify: `feature_list.json`
- Modify: `progress.md`
- Test: `scripts/check-harness-consistency.ps1`

**Step 1: Add the pending feature record**

Add `cache-infrastructure-extraction` with these definition-of-done items:

- `NL.Caching` has no non-BCL project/package dependency.
- `AnalysisRuntime` preserves per-compilation, per-cache-type reuse across runtime epochs.
- `CpgShardQueryResolver` preserves shard lookup order and byte-budget eviction behavior.
- Mark, structure-view, slice-query, frozen-shard, and builder caches remain in their owners.
- Focused cache contracts, sequential builds, and harness consistency checks pass.

**Step 2: Make `progress.md` concise**

Set the active feature to `cache-infrastructure-extraction`. Record the existing dirty-worktree constraint: stage only explicitly named cache files and never include unrelated scheduler or documentation edits.

**Step 3: Run the health check**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
pwsh -File .\init.ps1
```

Expected: the NLISSN build health check passes.

**Step 4: Commit only feature-state files**

```powershell
git add feature_list.json progress.md
git commit -m "Track cache infrastructure extraction"
```

**Acceptance gate:** Feature ownership and the dirty-worktree boundary are recorded before implementation begins.

## Phase 1: Add and prove BCL-only cache contracts

### Task 2: Create the project and weak typed registry

**Files:**

- Create: `src/NL.Caching/NL.Caching.csproj`
- Create: `src/NL.Caching/WeakTypedCacheRegistry.cs`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/RoslynDeletionPrototype.UnitTests.csproj`
- Create: `tests/RoslynDeletionPrototype.UnitTests/Caching/WeakTypedCacheRegistryTests.cs`

**Step 1: Write failing public-contract tests**

Add these tests under the `NL.Caching` namespace:

```csharp
[Fact]
public void GetOrCreate_ReusesTheValueForTheSameReferenceKeyAndValueType()
{
    var registry = new WeakTypedCacheRegistry<object>();
    var key = new object();

    var first = registry.GetOrCreate(key, _ => new CacheA("first"));
    var second = registry.GetOrCreate(key, _ => new CacheA("second"));

    Assert.Same(first, second);
}

[Fact]
public void GetOrCreate_PartitionsValuesByTypeAndKey()
{
    var registry = new WeakTypedCacheRegistry<object>();
    var firstKey = new object();
    var secondKey = new object();

    var firstA = registry.GetOrCreate(firstKey, _ => new CacheA("a"));
    var firstB = registry.GetOrCreate(firstKey, _ => new CacheB("b"));
    var secondA = registry.GetOrCreate(secondKey, _ => new CacheA("other"));

    Assert.NotSame(firstA, secondA);
    Assert.Equal("b", firstB.Value);
}
```

Do not write a GC-collection assertion: collection timing is nondeterministic and does not prove that a `ConditionalWeakTable` holds weak keys.

**Step 2: Verify the expected initial failure**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --filter FullyQualifiedName~WeakTypedCacheRegistryTests --no-restore
```

Expected: compilation fails because `NL.Caching` and `WeakTypedCacheRegistry` do not exist.

**Step 3: Add the project and the exact initial API**

Create a BCL-only project and implement this surface:

```csharp
namespace NL.Caching;

public sealed class WeakTypedCacheRegistry<TKey>
    where TKey : class
{
    public TValue GetOrCreate<TValue>(TKey key, Func<TKey, TValue> factory)
        where TValue : class;
}
```

Internally use `ConditionalWeakTable<TKey, ConcurrentDictionary<Type, object>>`. Validate `key` and `factory`, then preserve the current type-keyed `GetOrAdd` behavior. Do not use `Lazy<object>` in this task.

**Step 4: Reference the project directly from UnitTests**

Add only this test-time reference:

```xml
<ProjectReference Include="..\..\src\NL.Caching\NL.Caching.csproj" />
```

**Step 5: Re-run focused tests**

Expected: both registry tests pass.

**Step 6: Commit**

```powershell
git add src/NL.Caching/NL.Caching.csproj src/NL.Caching/WeakTypedCacheRegistry.cs tests/RoslynDeletionPrototype.UnitTests/RoslynDeletionPrototype.UnitTests.csproj tests/RoslynDeletionPrototype.UnitTests/Caching/WeakTypedCacheRegistryTests.cs
git commit -m "Add weak typed cache registry"
```

**Acceptance gate:** The new project compiles with no dependency beyond the BCL, and reuse is partitioned by weak key and value type.

### Task 3: Add a byte-budget LRU with deterministic behavior tests

**Files:**

- Create: `src/NL.Caching/ByteBudgetLruCache.cs`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/Caching/WeakTypedCacheRegistryTests.cs`
- Create: `tests/RoslynDeletionPrototype.UnitTests/Caching/ByteBudgetLruCacheTests.cs`

**Step 1: Write failing tests**

Cover these exact cases:

1. A zero budget retains no entry.
2. An entry with weight greater than budget is returned by `Set` as not cached and does not evict a fitting resident.
3. Accessing `A` after inserting `A` then `B` makes `B` the eviction victim when `C` is added.
4. Replacing an existing key adjusts byte accounting and creates one resident entry.
5. Parallel `TryGet`/`Set` calls complete without collection corruption; `CachedBytes` never exceeds budget.

Use integer keys/values and a small deterministic byte budget. Do not use sleeps.

**Step 2: Add only the required API**

```csharp
namespace NL.Caching;

public sealed class ByteBudgetLruCache<TKey, TValue>
    where TKey : notnull
{
    public ByteBudgetLruCache(long maxCachedBytes, IEqualityComparer<TKey>? comparer = null);

    public long CachedBytes { get; }
    public int Count { get; }
    public bool TryGet(TKey key, out TValue value);
    public bool Set(TKey key, TValue value, long byteWeight);
    public void Clear();
}
```

`Set` returns `true` only when the new value is retained. Reject negative weights with `ArgumentOutOfRangeException`. Keep a dictionary from key to list node, a linked list ordered LRU-to-MRU, and one byte counter behind a private lock.

**Step 3: Run focused unit contracts**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --filter "FullyQualifiedName~WeakTypedCacheRegistryTests|FullyQualifiedName~ByteBudgetLruCacheTests" --no-restore
```

Expected: all cache-primitive tests pass; no production project is referenced by `NL.Caching`.

**Step 4: Commit**

```powershell
git add src/NL.Caching/ByteBudgetLruCache.cs tests/RoslynDeletionPrototype.UnitTests/Caching
git commit -m "Add bounded byte-budget LRU cache"
```

**Acceptance gate:** Capacity, replacement, recency, and lock-protected accounting are independently proven before migrating a production owner.

## Phase 2: Migrate one owner at a time

### Task 4: Replace the private AnalysisRuntime registry

**Files:**

- Modify: `src/NLISSN.Core/NLISSN.Core.csproj`
- Modify: `src/NLISSN.Core/Pipeline/ExecutionRuntime.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Add failing host-level continuity tests**

Extend `AnalysisRuntime_GetOrCreateCompilationCache_AllowsMultipleCacheTypesPerCompilation` with assertions that the same cache type and `Compilation` instance are reused by both `runtime.InvalidateCaches()` and `runtime.NextEpoch()`.

**Step 2: Verify the test fails after asserting the intended contract**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --filter FullyQualifiedName~AnalysisRuntime_GetOrCreateCompilationCache --no-restore
```

Expected: the new epoch-continuity assertion exposes any accidental replacement of the registry.

**Step 3: Wire the generic registry without changing the public Core seam**

Add a direct `NL.Caching` project reference to `NLISSN.Core`. Replace the nested `RuntimeCacheRegistry` with `WeakTypedCacheRegistry<Compilation>`, retaining this method and its signature:

```csharp
public TCache GetOrCreateCompilationCache<TCache>(
    Compilation compilation,
    Func<Compilation, TCache> factory)
    where TCache : class;
```

Keep `CacheScopeKey`, `InvalidateCaches()`, and `NextEpoch()` unchanged. Remove `System.Collections.Concurrent` and `System.Runtime.CompilerServices` imports only after no longer needed by the file.

**Step 4: Run the focused host tests**

Expected: multiple cache types still coexist and all three runtime instances return the same compilation-scoped object.

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/NLISSN.Core.csproj src/NLISSN.Core/Pipeline/ExecutionRuntime.cs tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs
git commit -m "Use shared weak compilation cache registry"
```

**Acceptance gate:** Core preserves its existing cache API and epoch behavior while the reusable storage implementation moves below it.

### Task 5: Replace the resolver’s private LRU mechanics

**Files:**

- Modify: `src/NLCPG/NLCPG.csproj`
- Modify: `src/NLCPG/Analysis/CpgShardQueryResolver.cs`
- Modify: `tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGSliceQueryTests.cs`

**Step 1: Add resolver contracts before migration**

Add a local counting `ICpgShardStore` fake to `NLCPGSliceQueryTests` and cover:

1. Repeating the same node/symbol/span resolver request reads a fitting shard only once.
2. With a cache capacity of zero, repeated requests read the shard each time.
3. A shard larger than budget bypasses the cache and does not evict a previously cached fitting shard.
4. Resolver output remains sorted by `ShardId` even when the catalog returns locations in reverse order.

**Step 2: Run the focused tests and capture the baseline**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --filter FullyQualifiedName~NLCPGSliceQueryTests --no-restore
```

Expected: current contracts pass before replacing the internal structure.

**Step 3: Wire `ByteBudgetLruCache`**

Add a direct `NL.Caching` reference to `NLCPG`. Replace `_maxCachedBytes`, `_entries`, `_recency`, `_cachedBytes`, `AddToCache`, and the nested `CacheEntry` with:

```csharp
private readonly ByteBudgetLruCache<string, CpgFrozenShard> _cache;
```

Construct it from `maxCachedBytes`. In `OpenLocationsAsync`, call `_cache.TryGet(location.ShardId, out var shard)` before `_store.ReadAsync`; after a read call `_cache.Set(location.ShardId, shard, location.ByteLength)`. Keep the read outside any LRU lock and preserve the existing ordered loop.

**Step 4: Verify cache and query contracts**

Run the Task 5 focused command, then run:

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --filter FullyQualifiedName~CpgShardBuildCoordinatorTests --no-restore
```

Expected: shard-backed queries retain legacy/current build visibility and unchanged slice paths.

**Step 5: Commit**

```powershell
git add src/NLCPG/NLCPG.csproj src/NLCPG/Analysis/CpgShardQueryResolver.cs tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGSliceQueryTests.cs
git commit -m "Reuse bounded cache for shard reads"
```

**Acceptance gate:** The resolver owns I/O and ordering; the shared component owns only weighted retention and recency.

## Phase 3: Guard boundaries and complete verification

### Task 6: Add architecture guards and source inventory checks

**Files:**

- Create: `tests/RoslynDeletionPrototype.ContractTests/Architecture/CacheInfrastructureBoundaryTests.cs`

**Step 1: Add a failing project-boundary test**

Assert that `src/NL.Caching/NL.Caching.csproj` has no `ProjectReference` or `PackageReference`. Assert that all `src/NL.Caching/*.cs` sources contain neither `Microsoft.CodeAnalysis`, `NLCPG`, nor `NLISSN`.

**Step 2: Run the focused architecture test**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --filter FullyQualifiedName~ArchitectureBoundaryTests --no-restore
```

Expected: the new project passes the same dependency-boundary discipline as `NL.Concurrency`.

**Step 3: Run the targeted source inventory**

```powershell
rg -n "RuntimeCacheRegistry" src tests
rg -n "Dictionary<string, LinkedListNode<CacheEntry>>|private sealed record CacheEntry" src\NLCPG tests
```

Expected: the former Core registry and NLCPG-local LRU declarations are absent. `NL.Caching` is intentionally excluded because its bounded primitive owns its private cache-entry record.

**Step 4: Commit**

```powershell
git add tests/RoslynDeletionPrototype.ContractTests/Architecture/CacheInfrastructureBoundaryTests.cs
git commit -m "Guard cache infrastructure boundaries"
```

**Acceptance gate:** Future changes cannot reintroduce domain dependencies into the shared cache project or silently restore duplicated production LRU state.

### Task 7: Run sequential verification and record the result

**Files:**

- Modify: `feature_list.json`
- Modify: `progress.md`
- Modify: `docs/plans/2026-07-29-cache-infrastructure-extraction-execution-proposal.md`

**Step 1: Build production projects sequentially**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\NL.Caching\NL.Caching.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Application\NLISSN.Application.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
```

Expected: every build exits `0`. Run them sequentially because the repository shares Build output trees.

**Step 2: Run focused behavior suites sequentially**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --filter "FullyQualifiedName~Caching|FullyQualifiedName~StructureViewBuilderTests" --no-restore
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --filter FullyQualifiedName~PipelineComponentTests --no-restore
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --filter "FullyQualifiedName~NLCPGSliceQueryTests|FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~ArchitectureBoundaryTests" --no-restore
```

Expected: cache behavior, view invalidation, runtime compilation-cache continuity, shard queries, persistence equivalence, and project boundaries pass.

**Step 3: Check documentation and worktree integrity**

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
git status --short
```

Expected: harness and whitespace checks pass. The final status contains only files named by this plan plus any pre-existing user changes; do not stage the latter.

**Step 4: Record actual evidence**

Replace the planned commands in the feature record and `progress.md` with dates, test counts, warnings, failures, and any unrun command. Do not claim a memory or latency win without warmed same-fixture measurements.

**Step 5: Commit feature completion**

```powershell
git add feature_list.json progress.md docs/plans/2026-07-29-cache-infrastructure-extraction-execution-proposal.md
git commit -m "Complete cache infrastructure extraction"
```

**Acceptance gate:** The extracted infrastructure, the two migrations, and the boundary tests are green with evidence. Any performance conclusion remains separate until it has same-input warmed measurements.

## Execution record (2026-07-29)

- Built `NL.Caching`, `NLCPG`, `NLISSN.Core`, `NLISSN.Rules`, `NLISSN.Application`, and `NLISSN` sequentially with `--no-restore -p:UseSharedCompilation=false`: all passed with 0 warnings and 0 errors.
- Focused Unit tests for caching and structure views passed 26/26. The cache primitive subset itself passed 8/8.
- Focused Host `PipelineComponentTests` passed 116/116, including compilation-cache reuse across `InvalidateCaches()` and `NextEpoch()`.
- Focused Contract tests for shard queries, shard-build visibility, architecture boundaries, and cache infrastructure boundaries passed 59/59. The Contract build emitted pre-existing Verify/xUnit analyzer warnings only.
- `pwsh -File .\scripts\check-harness-consistency.ps1` and `git diff --check` passed. Source inventory found no `RuntimeCacheRegistry` or former NLCPG-local LRU declarations.
- No warmed benchmark was run and no throughput, allocation, retention, or latency claim is made by this feature.

## Risks and follow-up decisions

| Risk | Control in this plan | Deferred decision |
| --- | --- | --- |
| Generic registry changes factory execution under contention | Preserve the existing `ConcurrentDictionary.GetOrAdd` behavior in Phase 1 | Introduce a separate single-flight API only after callers define retry and exception semantics. |
| LRU lock causes I/O serialization | Restrict the primitive to in-memory get/set/list updates | Design a per-key async loader only if concurrent shard reads become a measured bottleneck. |
| Broad migration hides semantic cache keys | Migrate exactly two consumers | Review Mark, structure-view, and slice caches separately with their domain contracts. |
| Persistent output becomes stale | Keep persistence, sidecars, and manifests out of scope | Use source/config/format action keys in a dedicated persistence feature. |
| Refactor is called a performance improvement without evidence | Preserve capacity and ordering tests first | Benchmark warm hit rate, read latency, allocations, and retained bytes on the same source fixture. |

## Completion definition

This feature is complete only when `NL.Caching` remains BCL-only, both direct consumers retain their existing externally visible semantics, excluded caches remain in their owners, all listed verification commands have recorded outcomes, and no conclusion about performance is made without a separate measured baseline.
