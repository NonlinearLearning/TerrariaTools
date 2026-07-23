## [ERR-20260715-001] Start-Process standard-stream redirection

**Logged**: 2026-07-15T17:30:19+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
Background full-regression launch used the same file for standard output and standard error.

### Error
```
RedirectStandardOutput and RedirectStandardError are same. Give different inputs and Run your command again.
```

### Context
- Command: PowerShell `Start-Process dotnet test ...`
- No test process was created and no source files were changed.

### Resolution
- Use separate stdout and stderr log paths when running the regression in the background.

### Metadata
- Reproducible: yes
- Related Files: Build/full-regression-20260715.log

---

## [ERR-20260723-002] stale-no-build-baseline-output

**Logged**: 2026-07-23T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
`dotnet test --no-build` reported failures from an obsolete test assembly after source changes.

### Error
```
TextLogSystemTests.AnalyzeFromArgs_ForMultiFileDirectory_BatchesAnalysisLogRecordsAfterCompletion
Assert.True() Failure: io summary did not contain batches
```

### Context
- Current `AnalysisTextLogWriter` source already emitted the `batches` field.
- Rebuilding `RoslynDeletionPrototype.Tests.csproj` before rerunning the focused test made it pass.

### Resolution
- Build the test project before treating a `--no-build` result as a baseline whenever the worktree has changed since the last build.

### Metadata
- Reproducible: yes
- Related Files: tests/RoslynDeletionPrototype.Tests/RoslynDeletionPrototype.Tests.csproj, src/Host/Logging/AnalysisTextLogWriter.cs

---

## [ERR-20260718-002] harness-classify-change-array-binding

**Logged**: 2026-07-18T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: config

### Summary
Passing comma-separated paths or an un-splatted PowerShell array to the harness classifier did not bind the full path set.

### Error
```
Unclassified changed paths: src\\MinimalRoslynCpg\\...cs,tests\\RoslynDeletionPrototype.Tests\\...cs
```

### Resolution
- Use a hashtable and PowerShell splatting: `& .\\scripts\\harness-classify-change.ps1 @parameters` with `Paths = $paths`.

### Metadata
- Reproducible: yes
- Related Files: scripts/harness-classify-change.ps1

---

## [ERR-20260715-002] apply_patch empty move

**Logged**: 2026-07-15T18:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
`apply_patch` rejects a move-only update with no content hunk.

### Error
```
apply_patch verification failed: invalid hunk ... is empty
```

### Resolution
- Rename through an `apply_patch` move that also updates the archived file heading.

### Metadata
- Reproducible: yes
- Related Files: progress.md, progressinfo

---

## [ERR-20260717-001] RoslynPrototype build missing restore assets

**Logged**: 2026-07-17T16:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: config

### Summary
The deletion-rule executable could not build because its redirected intermediate output had no NuGet assets file.

### Error
```
NETSDK1004: 找不到资产文件 Build\src\obj\RoslynPrototype\project.assets.json
```

### Resolution
- Ran `dotnet restore .\src\RoslynPrototype\RoslynPrototype.csproj -p:UseSharedCompilation=false` before the build.

### Metadata
- Reproducible: yes after a clean redirected intermediate-output directory
- Related Files: Directory.Build.props, Build\src\obj\RoslynPrototype\project.assets.json

---

## [ERR-20260717-002] concurrent-diff-test-output-root

**Logged**: 2026-07-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The initial DOP-equivalence test compared `DiffFilePath` values produced with different configured diff roots.

### Error
```
Assert.Equal() Failure: Strings differ
Expected: concurrent-diff-output-1
Actual:   concurrent-diff-output-2
```

### Resolution
- Reuse one configured diff root across DOP runs; compare the captured relative diff bytes after each run.

### Metadata
- Reproducible: yes
- Related Files: tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs

---

## [ERR-20260718-001] github-readme-network-sandbox

**Logged**: 2026-07-18T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
The sandbox blocked direct HTTPS reads of official GitHub README files needed for documentation research.

### Error
```
curl: (7) Failed to connect to raw.githubusercontent.com port 443
```

### Context
- Attempted to read the React, FastAPI, and Kubernetes README sources from raw.githubusercontent.com.
- The user requested that the documentation rewrite be informed by high-star projects.

### Suggested Fix
Retry the same read-only requests with approved network access, then use only structural findings relevant to this repository.

### Metadata
- Reproducible: yes in the sandbox
- Related Files: README.md, docs/README.md

### Resolution
- **Resolved**: 2026-07-18T00:00:00+08:00
- **Notes**: The same read-only requests succeeded with approved network access; their structural findings informed the documentation portal.

---

## [ERR-20260718-002] nodeid-allocation-test-enum-order

**Logged**: 2026-07-18T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The new NodeId allocation test assumed a semantic kind order instead of the implementation's enum-value order.

### Error
```
Assert.Equal() Failure: Values differ
Expected: 1
Actual:   2
```

### Resolution
- Use one node kind with strictly ordered spans so the test isolates the documented stable-anchor ordering fields.

### Metadata
- Reproducible: yes
- Related Files: tests/RoslynDeletionPrototype.Tests/Cpg/RoslynCpgNodeIdContractTests.cs

---

## [ERR-20260719-001] codex-exec-textdecoder-unavailable

**Logged**: 2026-07-19T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
The Codex JavaScript execution isolate does not expose browser or Web text/Base64 decoding globals.

### Error
```
ReferenceError: TextDecoder is not defined
ReferenceError: atob is not defined
```

### Context
- Attempted to decode a Base64 JSON line before passing an exact one-line replacement to `apply_patch`.

### Resolution
- Have PowerShell emit the exact patch payload; keep the write itself in `apply_patch`.

### Metadata
- Reproducible: yes
- Related Files: feature_list.json

---

## [ERR-20260719-002] large-apply-patch-partial-cancellation

**Logged**: 2026-07-19T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A large multi-file delete patch did not return promptly and applied only an initial subset before cancellation.

### Resolution
- Use small logical batches, then enumerate the target directory before the next batch.

### Metadata
- Reproducible: unknown
- Related Files: docs/plans/

---

## [ERR-20260723-001] full-test-shard-catalog-rebuild-failure

**Logged**: 2026-07-23T00:00:00+08:00
**Priority**: high
**Status**: in_progress
**Area**: tests

### Summary
The full test project failed both shard-catalog rebuild durability variants and then the test host aborted.

### Error
```
Assert.True() Failure at CpgShardBuildCoordinatorTests.cs:39
BuildFromSource_Persistence_DeletedCatalog_RebuildsCompletedSession
Strict and Throughput both returned rebuilt == 0.
```

### Context
- Command: `dotnet test tests/RoslynDeletionPrototype.Tests/RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false`
- The newly added directory I/O/log performance regression passed before this full run.
- The failing test builds a persisted CPG, deletes `catalog.db`, then calls `RebuildFromShardHeadersAsync`.

### Suggested Fix
Reproduce the failing theory in isolation and inspect the completed shard headers and rebuild catalog path before changing persistence code.

### Metadata
- Reproducible: unknown
- Related Files: tests/RoslynDeletionPrototype.Tests/Cpg/CpgShardBuildCoordinatorTests.cs, src/MinimalRoslynCpg/Persistence/

### Investigation
- **2026-07-23**: The isolated theory passed both Strict and Throughput variants (2/2).
- **2026-07-23**: A second complete run passed 513 tests with zero assertion failures before the test host crashed.
- **Current boundary**: The failure requires the complete test host; it has not been reproduced by the persistence test itself.

---
