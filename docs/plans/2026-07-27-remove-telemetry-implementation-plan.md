# Remove Telemetry Implementation Plan

> **For Codex:** Execute the tasks in order. Preserve functional CPG, analysis,
> rewrite, persistence, and diagnostics behavior while removing telemetry.

**Goal:** Remove all telemetry collection, telemetry APIs, telemetry output, and
telemetry-only verification.

**Architecture:** Delete consumers before producers so each compiler failure
exposes the next dependency. Functional result models retain only analysis,
rewrite, persistence, and diagnostic data. Tests move from telemetry assertions
to behavior/equivalence assertions or are removed when their sole purpose is
measurement.

**Tech Stack:** .NET 10, Roslyn, xUnit, SQLite.

---

### Task 1: Remove NLISSN runtime telemetry output

**Files:**
- Delete: `src/NLISSN/Telemetry/AnalysisTextLogWriter.cs`
- Delete: `src/NLISSN/Telemetry/RunTextLogWriter.cs`
- Modify: `src/NLISSN/CommandHost.cs`
- Modify: `src/NLISSN/DirectoryAnalysisService.cs`
- Test: `tests/Roslyn Prototype.HostTests/Logging/TextLogSystemTests.cs`

1. Remove the command-host log sink, writer, context, flush, and exception-log
   paths while retaining analysis, diff, and exception propagation.
2. Remove the optional directory analysis writer and all its calls.
3. Delete tests whose only observed behavior is telemetry output; retain any
   functional log infrastructure test needed outside this feature.
4. Run the focused HostTests project.

### Task 2: Remove analysis and query telemetry contracts

**Files:**
- Modify: `src/NLISSN.Core/Rewrite/PrototypeAnalysisResult.cs`
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
- Modify: `src/NLISSN.Core/Pipeline/RuleContext.cs`
- Modify: `src/NLISSN.Core/Analysis/MarkAnalysisSnapshot.cs`
- Modify: `src/NLISSN.Core/Analysis/View/NLCPGStructureViewBuilder.cs`
- Modify: `src/NLCPG/Analysis/CpgShardQueryResolver.cs`
- Modify: `src/NLCPG/Analysis/NLCPGSliceQuery.cs`
- Modify: `src/NLCPG/Analysis/NLCPGSliceQueryOptions.cs`

1. Remove telemetry properties from result/context records and their creation,
   merging, and propagation paths.
2. Retain query cache behavior but remove its counters and telemetry result.
3. Retain mark snapshots and structure-view caching but remove measurement-only
   counters and snapshots.
4. Replace affected test assertions with graph/query/analysis result assertions.

### Task 3: Remove CPG build and persistence telemetry

**Files:**
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Modify: `src/NLCPG/Builder/BoundedPartitionWorkWindow.cs`
- Modify: `src/NLCPG/Builder/CpgShardBuildCoordinator.cs`
- Modify: `src/NLCPG/Builder/CpgShardBuildSession.cs`
- Modify: `src/NLCPG/Builder/Passes/*.cs`
- Modify: `src/NLCPG/Model/NLCPGFreezeTelemetry.cs`
- Modify: `src/NLCPG/Model/NLCPGGraph.cs`
- Modify: `src/NLCPG/Model/NLCPGGraphIndex.cs`
- Modify: `src/NLCPG/Persistence/CpgShardContracts.cs`
- Modify: `src/NLCPG/Persistence/CpgShardStore.cs`
- Modify: `src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs`

1. Remove `NLCPGBuildTelemetry`, pass telemetry, work-window telemetry,
   admission telemetry, shard write/read telemetry, and catalog-stage telemetry.
2. Keep synchronization, persistence validation, catalog writes, cache behavior,
   and all deterministic ordering intact after removing timers and counters.
3. Simplify return types and call signatures rather than retaining no-op
   telemetry parameters.
4. Run focused ContractTests for CPG construction, persistence, and slicing.

### Task 4: Remove benchmark and telemetry-only test surface

**Files:**
- Delete or simplify: `tools/CpgPersistenceBenchmark/Program.cs`
- Modify: `tools/CpgPersistenceBenchmark/CpgPersistenceBenchmark.csproj`
- Modify: telemetry-referencing test files under `tests/`
- Modify: telemetry-named fixtures in
  `tests/Roslyn Prototype.Testing/TestCodeSet/Cpg/CpgBuilderSources.cs`

1. Remove the benchmark program and its project reference if no functional
   tooling remains.
2. Delete telemetry-only tests and fixtures; preserve behavior tests that happen
   to use telemetry-named source text by renaming their fixtures.
3. Confirm no source, test, or tool code still contains `Telemetry`,
   `LastBuildTelemetry`, or telemetry-writer references.

### Task 5: Verify the deletion

1. Run `git diff --check`.
2. Run the smallest owning Unit, Contract, and Host test projects, then the
   repository test tiers that are available in the migrated worktree.
3. Build every remaining `src/**/*.csproj` and `tests/**/*.csproj` that can be
   discovered from the current checkout.
4. Report any project blocked by the pre-existing deleted
   `src/MinimalRoslynCpg` migration separately from telemetry cleanup failures.
