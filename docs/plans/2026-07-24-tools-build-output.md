# Tools Build Output Implementation Plan

**Goal:** Route the repository tool projects' build and intermediate outputs into the shared `Build` tree.

**Architecture:** Extend the existing central `Directory.Build.props` path-based output policy with a `tools/` rule. Preserve the project-specific intermediate path segment, then remove only the generated `bin` and `obj` directories that the new policy supersedes.

**Tech Stack:** MSBuild, .NET SDK 10

---

### Task 1: Centralize tool output paths

**Files:**
- Modify: `Directory.Build.props`

**Step 1: Establish the failing baseline**

Run: `dotnet msbuild .\tools\CpgMicrobenchmarks\CpgMicrobenchmarks.csproj -getProperty:BaseOutputPath`

Expected: The property does not resolve beneath `Build\tools`.

**Step 2: Add the minimal centralized rule**

Add a path-based `tools/` property group that sets `BaseOutputPath` to `Build\tools\` and `BaseIntermediateOutputPath` to `Build\tools\obj\$(MSBuildProjectName)\`.

**Step 3: Verify effective properties and builds**

Run the property query and `dotnet build --no-restore` for both tool projects. Confirm their build artifacts appear under `Build\tools`.

### Task 2: Remove superseded local generated artifacts

**Files:**
- Delete: `tools\CpgMicrobenchmarks\bin\`
- Delete: `tools\CpgMicrobenchmarks\obj\`
- Delete: `tools\CpgPersistenceBenchmark\bin\`
- Delete: `tools\CpgPersistenceBenchmark\obj\`
- Delete: `tools\CpgPersistenceBenchmark\Build\`

**Step 1: Verify targets are generated and ignored**

Inspect Git status and `.gitignore` before deletion.

**Step 2: Remove only the five listed generated directories**

Remove the local `Build` directory only after verifying it contains no benchmark reports. Its `catalog-telemetry-*` children are compiler outputs from previous explicit `-o` builds and are superseded by `Build\tools`.

**Step 3: Verify cleanup**

Rebuild both projects and confirm no `bin` or `obj` directories are recreated below either tool project.
