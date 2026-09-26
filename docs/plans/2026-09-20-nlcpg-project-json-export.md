# NLCPG Project JSON Export Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement the plan task-by-task.

**Goal:** Add a project-level NLCPG exporter that analyzes a `.csproj` with its Roslyn compilation and writes mirrored per-source-file JSON plus a project manifest.

**Architecture:** Keep the existing `NLCPG` single-file CLI and graph model unchanged. Add an independent `NLCPG.ProjectExport` executable that reuses `NLISSN.Workspace` for `MSBuildWorkspace` loading and `NLCPGBuilder.BuildFromSemanticModel` for each non-generated C# document. Per-file documents contain local graph payloads with stable global references; `manifest.json` contains project metadata, file status, deduplicated global nodes, cross-file edge references, and diagnostics.

**Tech Stack:** .NET 10, Roslyn 4.14, `MSBuildWorkspace`, `System.Text.Json`, xUnit contract tests.

---

### Task 1: Lock the export contract with a failing test

**Files:**
- Create: `tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportTests.cs`
- Modify: `tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj`
- Create: `src/NLCPG.ProjectExport/NLCPG.ProjectExport.csproj`

**Step 1: Write the failing test**

Add a contract test that runs the exporter against the existing `WorkspaceFixture` App project and asserts that:

- the source-relative `App.cs.json` is written under the requested output directory;
- the per-file JSON contains `schemaVersion`, `sourcePath`, non-empty `nodes`, and `edges` with global string references;
- `manifest.json` contains one project, the mirrored file entry, a global node index, and the generated output paths use `/` separators;
- the test project can reference the new exporter project.

**Step 2: Run the focused test to verify it fails**

Run the serial wrapper for `tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj` with the focused test filter. Expected: compilation fails because the exporter project and `ProjectJsonExporter` do not exist yet.

### Task 2: Implement the project exporter

**Files:**
- Create: `src/NLCPG.ProjectExport/ProjectJsonExporter.cs`
- Create: `src/NLCPG.ProjectExport/ProjectExportOptions.cs`
- Create: `src/NLCPG.ProjectExport/ProjectExportResult.cs`

**Step 1: Implement typed export options and result models**

Support project path, output directory, configuration, platform, optional target framework, restore mode, and generated-source inclusion. Default output is `<project directory>/Build/NLCPG-json`; default generated-source mode excludes generated documents.

**Step 2: Implement workspace loading**

Use `MsBuildWorkspaceInputLoader.LoadAsync` with `RequireCleanCompilation: false`, preserve loader diagnostics, select the requested project snapshot, and collect Roslyn compilation diagnostics without aborting the whole export.

**Step 3: Implement per-file graph extraction**

For each source `.cs` document, obtain its project compilation semantic model and syntax root, call `NLCPGBuilder.BuildFromSemanticModel`, project all nodes and edges to JSON-safe DTOs, and write `<relative source path>.json` below the output mirror. Catch document-level failures and record them in the manifest.

**Step 4: Implement stable global references**

Create a deterministic SHA-256-based node reference from normalized project-relative file identity, node kind, source span, semantic names/signature/type, implicitness, and duplicate occurrence ordinal. Use those references in file edges and manifest edges, so local `NodeId` collisions across files cannot corrupt the global catalog.

**Step 5: Implement manifest writing**

Write a deterministic `manifest.json` with schema version, project metadata, source file entries, deduplicated global node records, all edge references, counts, and diagnostics. Keep output paths relative and slash-normalized. Return a non-zero CLI result only when the project cannot be loaded; individual document failures remain represented in the manifest.

### Task 3: Add the command-line entry point and documentation

**Files:**
- Create: `src/NLCPG.ProjectExport/Program.cs`
- Modify: `docs/cli-reference.md`
- Modify: `docs/quick-start.md`

**Step 1: Implement CLI parsing**

Support `--project`, `--output`, `--target-framework`, `--configuration`, `--platform`, `--restore`, `--include-generated`, and `--help`. Print counts, diagnostics, and the manifest path.

**Step 2: Document the Version4 command**

Document the default mirror layout and a concrete command for `D:\TRbackup\Version4\TerrariaServer.csproj` with output at `D:\TRbackup\Version4\Build\NLCPG-json`.

### Task 4: Verify and run the requested Version4 export

**Files:**
- No additional source files.

**Step 1: Run the focused contract test**

Run it through the repository serial wrapper and confirm the test passes.

**Step 2: Build the affected exporter project**

Build only `src/NLCPG.ProjectExport/NLCPG.ProjectExport.csproj`, verify the artifact under `Build/bin`, and record exit code and warning/error counts.

**Step 3: Run the exporter on Version4**

Analyze `D:\TRbackup\Version4\TerrariaServer.csproj` and write to `D:\TRbackup\Version4\Build\NLCPG-json`.

**Step 4: Validate output**

Parse `manifest.json`, verify source/output path mirroring, count `.cs.json` files against manifest written entries, verify all manifest edge endpoints resolve to manifest node IDs, and report compilation/document diagnostics without treating them as hidden failures.
