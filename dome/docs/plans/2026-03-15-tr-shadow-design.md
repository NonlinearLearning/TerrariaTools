## TR Shadow Design

### Goal
- Add a `tr-shadow` CLI entry that extracts a DedServ-oriented shadow project into a separate runtime workspace.
- Implement it as a new internal application service in the current dome architecture.
- First version is document-level extraction, not member-level slicing.

### Scope
- Fixed seed method: `Terraria.Main.DedServ`
- Fixed default output root: `.tmp\\tr-shadow`
- Preserve non-`.cs` files and directory structure
- Copy only included `.cs` documents into the shadow workspace
- Write a dedicated shadow extraction report artifact

### Architecture
- New CLI command: `tr-shadow`
- New request/layout types:
  - `TerrariaRuntimeShadowExtractionRequest`
  - `TerrariaRuntimeShadowLayout`
- New internal services:
  - `TerrariaRuntimeShadowExtractionApplication`
  - `TerrariaRuntimeShadowProjectBuilder`
- Reuse:
  - `IWorkspaceLoader`
  - `RoslynAnalysisEngine`
  - `AnalysisContext.MethodCalls`
  - `FunctionIndex`

### Extraction Strategy
1. Load the TR solution through the current workspace loader.
2. Analyze the solution with the current Roslyn analysis engine.
3. Resolve the seed method by matching `MemberId` against `Terraria.Main.DedServ`.
4. Compute reachable methods from that seed through `MethodCalls`.
5. Map reachable methods to source documents through `FunctionIndex`.
6. Build the shadow workspace:
   - copy all non-`.cs` files
   - copy only included `.cs` files
   - keep relative paths unchanged
7. Write a JSON report with seed, reachable methods, included documents, and advanced analysis summary.

### Non-Goals
- No member-level shadow source generation
- No new multi-tool platform
- No integration into `tr-run`
- No new delete/rewrite semantics

### Testing
- CLI parser accepts `tr-shadow`
- Application extracts only reachable `.cs` files
- Non-`.cs` files are preserved
- Report artifact is written with the expected seed and document list
