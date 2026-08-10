# Project-Backed Single-Document Analysis Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Analyze one `.cs` document with its owning MSBuild project's complete Roslyn semantic compilation.

**Architecture:** Keep the existing standalone `.cs` and directory paths compatible. Add an optional project context for a `.cs` input, load that project through the existing Infrastructure Workspace loader, retain the full project compilation, and analyze only the requested document. Fail closed when the selected compilation has unresolved error diagnostics. Keep MSBuild APIs in Infrastructure and preserve generated-source no-writeback rules.

**Tech Stack:** .NET 10 preview, Roslyn, MSBuildWorkspace, YamlDotNet, xUnit.

---

### Task 1: Add red regression coverage

**Files:**
- Modify: `tests/NLISSN.HostTests/Workspace/WorkspaceConfigurationTests.cs`
- Modify: `tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisHostTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`

**Steps:**
1. Add a configuration test accepting `.cs` input with `input.project` and resolving both paths.
2. Add a host test selecting one project document while resolving a symbol declared in the referenced Library project.
3. Add a host test proving an unresolved standalone compilation fails closed instead of silently producing semantic-rule output.
4. Run the focused tests and confirm they fail because the new contract and routing do not exist.

### Task 2: Extend project-backed input contracts

**Files:**
- Modify: `src/NLISSN.Infrastructure/Workspace/WorkspaceInputOptions.cs`
- Modify: `src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs`
- Modify: `src/NLISSN.Infrastructure/Configuration/ResolvedConfigurationArtifact.cs`
- Modify: `schemas/nlissn.schema.2.json`

**Steps:**
1. Add a target document path to `WorkspaceInputOptions` while retaining the project/solution path used by MSBuild.
2. Permit `input.project` only for `.sln`, `.csproj`, or `.cs` whose project path is explicitly supplied; validate that the target document and project are related.
3. Build Workspace options for a `.cs` target by loading its owning `.csproj`.
4. Persist target-document provenance in `resolved-configuration.json` and update the schema.

### Task 3: Route one document through the real compilation

**Files:**
- Modify: `src/NLISSN/Hosting/CommandHost.cs`
- Modify: `src/NLISSN/Hosting/WorkspaceAnalysisService.cs`
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`

**Steps:**
1. Route `.cs` plus project context through Workspace before the legacy standalone branch.
2. Filter the project document snapshot to the requested document, but pass the unfiltered project `CSharpCompilation` into `AnalyzeCompiled`.
3. Preserve generated-document writeback and rewrite-plan exclusion.
4. Reject cross-file writeback from single-document mode unless the requested result is local to the selected document.

### Task 4: Add semantic fail-closed diagnostics

**Files:**
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
- Add or modify focused tests in `tests/NLISSN.HostTests/`

**Steps:**
1. Check compilation errors before semantic rule execution for project-backed input.
2. Emit a stable diagnostic with project/document context and stop analysis when required symbols are unresolved.
3. Keep legacy standalone analysis available, but mark it as best-effort and do not claim project semantic completeness.

### Task 5: Update user-facing documentation and verify

**Files:**
- Modify: `docs/quick-start.md`
- Modify: `docs/cli-reference.md`
- Modify: `docs/developer-guide.md`
- Modify: `progress.md`
- Modify: `feature_list.json`

**Steps:**
1. Document `.cs` plus `input.project`, project compilation scope, and rewrite limitations.
2. Run focused Workspace/Host tests, project builds, full Host and Contract tiers, and harness consistency.
3. Record only current verification facts and remaining boundaries in `progress.md` and `feature_list.json`.
