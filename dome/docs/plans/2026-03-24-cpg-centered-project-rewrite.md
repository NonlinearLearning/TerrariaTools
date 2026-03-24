# CPG-Centered Dome Rewrite Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rewrite `dome` so `src/Core/CPG` becomes the real analysis core, while preserving the layered application shape established by the existing multi-project commits.

**Architecture:** Keep the outer split introduced by the historical refactors: `apps/` hosts, `src/Application/` use-case orchestration, `src/Adapters/` infrastructure, and `src/Core/` domain models/services. Replace the current ad-hoc Roslyn analysis snapshot builder with a CPG-backed pipeline centered on `src/Core/CPG`, then project CPG facts into the existing `Core.Analysis` contracts so rules, planning, rewrite, runtime, and shadow extraction can migrate without a flag day.

**Tech Stack:** C#, .NET 10, Roslyn, xUnit, solution-based multi-project layout, in-memory CPG runtime.

---

## Historical Anchors

Use these commits as the non-negotiable baseline when deciding what must survive the rewrite:

1. `54ad62c` (`2026-03-12`): first large split from single-project tool to `Analysis/Application/Cli/Core/Plan/Reporting/Rewrite/Rules`.
2. `63b25c8` (`2026-03-15`): phase-one stabilization; runtime application, shadow extraction support, naming cleanup, more samples and tests.
3. `2b25128` (`2026-03-18`): current layered direction; `apps/ + src/Application + src/Adapters + src/Core`, removal of the old `src/Core` monolith, contract/test refactor.

The rewrite must preserve the usable value from those commits:

- Standard `run / analyze / plan` flow.
- Runtime host and shadow-extraction host.
- Existing rule/planning/rewrite contract shape.
- Current automated test investment.

## Verified Baseline Before Any Rewrite Work

These facts were verified in the current worktree and should be treated as the starting point:

1. `src/Core/CPG` is already present and largely implemented, but it is still branded as `TerrariaTools.Dome.Prototypes.JoernishCpg`.
2. `dotnet sln dome.sln list` does **not** include any `src/Core/CPG` project, so the new core is not part of the main solution cutover yet.
3. `dotnet test dome/src/Core/CPG/Tests/JoernishCpg.Tests.csproj /p:RestoreIgnoreFailedSources=true` currently yields `96` passed and `2` failed tests.
4. Both failing tests are path-regression failures in [`CodeGenerationTests.cs`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\CPG\Tests\Schema\CodeGenerationTests.cs), still reading from `dome/prototypes/JoernishCpg/Generated/...`.
5. [`ApplicationDefaultServices.cs`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\apps\Dome.Application\Composition\ApplicationDefaultServices.cs) still wires the standard host to `new RoslynAnalysisEngine()`, so the application stack is not using `src/Core/CPG`.
6. [`RoslynAnalysisEngine.cs`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Adapters\Analysis.Roslyn\RoslynAnalysisEngine.cs) still builds `AnalysisOutput` with a large in-file `BuildContext`, not from a CPG projection.

## Global Verification Commands

Use these commands exactly during execution:

```powershell
$env:DOTNET_CLI_HOME='D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\.dotnet-cli-home'
$env:HOME='D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\.dotnet-home'
```

Targeted CPG verification:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\CPG\Tests\JoernishCpg.Tests.csproj /p:RestoreIgnoreFailedSources=true
```

Targeted application verification:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj /p:RestoreIgnoreFailedSources=true
```

Solution verification:

```powershell
dotnet build D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln /p:RestoreIgnoreFailedSources=true
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln /p:RestoreIgnoreFailedSources=true
```

Expected steady-state result: `PASS`, with only the non-blocking preview SDK warning `NETSDK1057` and possible offline vulnerability warning `NU1900`.

## Task 1: Promote `src/Core/CPG` from Prototype Copy to Real Core Package

**Files:**
- Move: `dome/src/Core/CPG/JoernishCpg.Prototype.csproj` -> `dome/src/Core/CPG/Dome.Core.Cpg.csproj`
- Modify: `dome/src/Core/CPG/AssemblyMarker.cs`
- Modify: `dome/src/Core/CPG/Schema/CpgCodeGenerator.cs`
- Modify: `dome/src/Core/CPG/Tests/JoernishCpg.Tests.csproj`
- Modify: `dome/src/Core/CPG/Tests/Schema/CodeGenerationTests.cs`
- Modify: `dome/src/Core/CPG/**/*.cs` to replace `TerrariaTools.Dome.Prototypes.JoernishCpg` with `TerrariaTools.Dome.Core.Cpg`
- Modify: `dome/dome.sln`

**Step 1: Write the failing tests**

Add a regression test that proves the migrated package no longer depends on the deleted prototype path:

```csharp
[Fact]
public void GeneratedFileRegression_ShouldResolveFilesFromCurrentCoreCpgRoot()
{
    string path = CpgProjectLayout.GeneratedFile("NodeKinds.g.cs");
    Assert.True(File.Exists(path), path);
}
```

Tighten the identity test so the assembly no longer exposes prototype naming:

```csharp
[Fact]
public void AssemblyMarker_ShouldExposeCoreCpgIdentity()
{
    Assert.Equal("Dome.Core.Cpg", AssemblyMarker.Value);
}
```

**Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\CPG\Tests\JoernishCpg.Tests.csproj --filter "FullyQualifiedName~CodeGenerationTests|FullyQualifiedName~AssemblyMarkerTests" /p:RestoreIgnoreFailedSources=true
```

Expected: `FAIL`, because tests still point at `dome/prototypes/JoernishCpg` and the assembly/project identity is still prototype-branded.

**Step 3: Write minimal implementation**

Create a single layout helper and stop hard-coding old paths:

```csharp
internal static class CpgProjectLayout
{
    public static string ProjectRoot { get; } = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "dome", "src", "Core", "CPG"));

    public static string GeneratedFile(string fileName) => Path.Combine(ProjectRoot, "Generated", fileName);
}
```

Set the package identity to the real core name:

```xml
<AssemblyName>Dome.Core.Cpg</AssemblyName>
<RootNamespace>TerrariaTools.Dome.Core.Cpg</RootNamespace>
```

**Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\CPG\Tests\JoernishCpg.Tests.csproj --filter "FullyQualifiedName~CodeGenerationTests|FullyQualifiedName~AssemblyMarkerTests" /p:RestoreIgnoreFailedSources=true
dotnet sln D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln list
```

Expected: filtered tests `PASS`, and the solution list now includes `src\Core\CPG\Dome.Core.Cpg.csproj`.

**Step 5: Commit**

```bash
git add dome/src/Core/CPG dome/dome.sln
git commit -m "refactor: promote cpg prototype into core package"
```

## Task 2: Extend Core Analysis Runtime to Carry the CPG

**Files:**
- Modify: `dome/src/Core/Analysis/AnalysisRuntimeModels.cs`
- Modify: `dome/src/Core/Analysis/Dome.Model.Analysis.csproj`
- Modify: `dome/src/Application/Ports/Dome.Application.Abstractions.csproj`
- Test: `dome/tests/Dome.Tests/Application/Contracts/PublicContractBoundaryTests.cs`

**Step 1: Write the failing test**

Add a contract test that proves analysis output can expose the backing CPG without changing the current outer API shape:

```csharp
[Fact]
public async Task AnalysisOutput_ShouldExposeBackedCodePropertyGraph()
{
    var engine = new RoslynAnalysisEngine();
    var input = TestAnalysisInputs.SingleDocument("class C { void M() { } }");

    var output = await engine.AnalyzeAsync(input, CancellationToken.None);

    Assert.NotNull(output.Snapshot.CodePropertyGraph);
    Assert.Contains("base", output.Snapshot.CodePropertyGraph.MetaData.Overlays);
}
```

**Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~PublicContractBoundaryTests" /p:RestoreIgnoreFailedSources=true
```

Expected: `FAIL`, because `AnalysisExecutionSnapshot` currently has no CPG field.

**Step 3: Write minimal implementation**

Extend the runtime snapshot, but keep existing consumers stable:

```csharp
public sealed record AnalysisExecutionSnapshot(
    AnalysisResultModel View,
    FunctionIndex FunctionIndex,
    FunctionFactsIndex FunctionFacts,
    StatementFactsIndex StatementFacts,
    DomeCpg CodePropertyGraph);
```

Expose a convenience property on `AnalysisOutput`:

```csharp
public DomeCpg CodePropertyGraph => Snapshot.CodePropertyGraph;
```

**Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~PublicContractBoundaryTests" /p:RestoreIgnoreFailedSources=true
```

Expected: `PASS`.

**Step 5: Commit**

```bash
git add dome/src/Core/Analysis dome/src/Application/Ports dome/tests/Dome.Tests/Application/Contracts/PublicContractBoundaryTests.cs
git commit -m "feat: carry cpg through analysis runtime contracts"
```

## Task 3: Replace the In-File Roslyn Snapshot Builder with a CPG Projection Layer

**Files:**
- Create: `dome/src/Adapters/Analysis.Roslyn/CpgProjection/CpgAnalysisProjection.cs`
- Create: `dome/src/Adapters/Analysis.Roslyn/CpgProjection/RoslynCpgProjector.cs`
- Modify: `dome/src/Adapters/Analysis.Roslyn/Dome.Analysis.Roslyn.csproj`
- Modify: `dome/src/Adapters/Analysis.Roslyn/RoslynAnalysisEngine.cs`
- Modify: `dome/src/Adapters/Analysis.Roslyn/FunctionGraphProvider.cs`
- Modify: `dome/src/Adapters/Analysis.Roslyn/StatementAnalysisService.cs`
- Test: `dome/tests/Dome.Tests/Analysis/Integration/AnalysisNativePathTests.cs`
- Test: `dome/tests/Dome.Tests/Analysis/Contracts/AnalysisEngineContractTests.cs`

**Step 1: Write the failing tests**

Add an integration test that proves the returned function graph is projected from CPG call edges, not the old `BuildContext` side tables:

```csharp
[Fact]
public async Task AnalyzeAsync_ShouldProjectFunctionGraphFromCpgCallEdges()
{
    var engine = new RoslynAnalysisEngine();
    var input = TestAnalysisInputs.SingleDocument("class C { void A() { } void B() { A(); } }");

    var output = await engine.AnalyzeAsync(input, CancellationToken.None);

    Assert.Contains(output.CodePropertyGraph.Edges, edge => edge.Label == EdgeKinds.Call);
    Assert.Contains(output.View.FunctionGraph.Edges, edge => edge.TargetMemberId.Value.EndsWith(".A", StringComparison.Ordinal));
}
```

**Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~AnalysisNativePathTests|FullyQualifiedName~AnalysisEngineContractTests" /p:RestoreIgnoreFailedSources=true
```

Expected: `FAIL`, because `RoslynAnalysisEngine` still builds analysis data through the current in-file `BuildContext`.

**Step 3: Write minimal implementation**

Split the job into two explicit parts:

```csharp
public sealed class RoslynCpgProjector
{
    public DomeCpg Build(SourceDocumentSet sourceSet)
    {
        var frontend = new RoslynCSharpFrontend();
        var cpg = frontend.CreateCpg(new RoslynFrontendConfig(sourceSet.Documents));
        DefaultOverlays.Apply(cpg, new CpgContext(cpg, BuiltinSchema.Create()));
        return cpg;
    }
}
```

```csharp
public sealed class CpgAnalysisProjection
{
    public AnalysisOutput Project(DomeCpg cpg, SourceDocumentSet sourceSet)
    {
        // Map CPG nodes and edges into AnalysisResultModel, indexes, and services.
    }
}
```

Then shrink `RoslynAnalysisEngine` to orchestration only.

**Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~AnalysisNativePathTests|FullyQualifiedName~AnalysisEngineContractTests" /p:RestoreIgnoreFailedSources=true
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\CPG\Tests\JoernishCpg.Tests.csproj /p:RestoreIgnoreFailedSources=true
```

Expected: both suites `PASS`.

**Step 5: Commit**

```bash
git add dome/src/Adapters/Analysis.Roslyn dome/tests/Dome.Tests/Analysis
git commit -m "refactor: back roslyn analysis engine with cpg projection"
```

## Task 4: Rewire the Standard, Runtime, and Shadow-Extraction Pipelines to the CPG-Backed Engine

**Files:**
- Modify: `dome/apps/Dome.Application/Composition/ApplicationDefaultServices.cs`
- Modify: `dome/src/Application/Pipeline/DomeApplicationStages.cs`
- Modify: `dome/src/Application/UseCases/Runtime/TerrariaRuntimeApplicationStages.cs`
- Modify: `dome/src/Application/UseCases/ShadowExtraction/ShadowExtractionWrappers.cs`
- Test: `dome/tests/Dome.Tests/Application/Integration/DomeApplicationTests.cs`
- Test: `dome/tests/Dome.Tests/Application/Integration/TerrariaRuntimeApplicationTests.cs`
- Test: `dome/tests/Dome.Tests/Application/Integration/TerrariaRuntimeShadowExtractionApplicationTests.cs`

**Step 1: Write the failing tests**

Add an end-to-end assertion that the standard host, runtime host, and shadow-extraction flow all observe the CPG-backed snapshot:

```csharp
[Fact]
public async Task DomeApplication_ShouldPreserveCpgBackedAnalysisAcrossPipeline()
{
    var app = DomeApplicationFactory.CreateDefault();
    var result = await app.RunAsync(TestRunRequests.AnalyzeOnlySample("expression-loop"));

    Assert.True(result.IsSuccess);
    Assert.NotNull(result.AnalysisOutput);
    Assert.NotNull(result.AnalysisOutput.CodePropertyGraph);
}
```

**Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~DomeApplicationTests|FullyQualifiedName~TerrariaRuntimeApplicationTests|FullyQualifiedName~TerrariaRuntimeShadowExtractionApplicationTests" /p:RestoreIgnoreFailedSources=true
```

Expected: `FAIL`, because the composition root and pipeline still assume the old analysis implementation boundary.

**Step 3: Write minimal implementation**

Keep host contracts stable and only swap the backing engine:

```csharp
public static ApplicationAbstractions.IAnalysisEngine CreateAnalysisEngine() =>
    new RoslynAnalysisEngine(new RoslynCpgProjector(), new CpgAnalysisProjection());
```

Do not let pipeline stages parse CPG internals directly. They should consume the same `AnalysisOutput` shape and only rely on the newly added `CodePropertyGraph` when required.

**Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~DomeApplicationTests|FullyQualifiedName~TerrariaRuntimeApplicationTests|FullyQualifiedName~TerrariaRuntimeShadowExtractionApplicationTests" /p:RestoreIgnoreFailedSources=true
```

Expected: `PASS`.

**Step 5: Commit**

```bash
git add dome/apps/Dome.Application dome/src/Application dome/tests/Dome.Tests/Application
git commit -m "refactor: route dome pipelines through cpg-backed analysis"
```

## Task 5: Remove Legacy Drift and Align Documentation, Solution Layout, and CI

**Files:**
- Modify: `dome/.github/workflows/dome-ci.yml`
- Modify: `dome/docs/architecture/overview.md`
- Modify: `dome/docs/architecture/project-layout.md`
- Modify: `dome/docs/guides/build-and-test.md`
- Modify: `dome/docs/plans/2026-03-24-joernish-cpg.md`
- Modify: `dome/docs/plans/2026-03-24-joernish-cpg-gap-plan.md`
- Modify: `dome/docs/plans/2026-03-24-joernish-cpg-third-stage-closure.md`
- Modify: `dome/docs/plans/2026-03-24-joernish-cpg-final-gap-closure.md`
- Delete or stop referencing: legacy tracked tree under `dome/src/Analysis`, `dome/src/Model`, `dome/src/Rules`, `dome/src/Rewrite`, `dome/src/Reporting`, `dome/src/Cli`, and any leftover `Legacy` folders that duplicate the new solution layout
- Test: `dome/tests/Dome.Tests/Cli/Unit/DomeCliApplicationExecutorTests.cs`
- Test: `dome/tests/TerrariaTools.Testing/Assertions/TestSuiteLayoutAuditor.cs`

**Step 1: Write the failing tests**

Add a layout/contract test that proves the repository no longer documents or executes the deleted prototype path:

```csharp
[Fact]
public void DocumentationAndCli_ShouldReferenceCoreCpgPathsOnly()
{
    string docs = File.ReadAllText("dome/docs/guides/build-and-test.md");
    Assert.DoesNotContain("dome/prototypes/JoernishCpg", docs, StringComparison.Ordinal);
    Assert.Contains("dome/src/Core/CPG", docs, StringComparison.Ordinal);
}
```

**Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~DomeCliApplicationExecutorTests|FullyQualifiedName~TestSuiteLayoutAudit" /p:RestoreIgnoreFailedSources=true
```

Expected: `FAIL`, because docs and plan files still point to `dome/prototypes/JoernishCpg` and CI does not yet validate the migrated core.

**Step 3: Write minimal implementation**

Update every plan, guide, workflow, and solution entry to reflect the real cutover path:

```text
dome/src/Core/CPG
```

Make the CI workflow run:

```powershell
dotnet test dome/src/Core/CPG/Tests/JoernishCpg.Tests.csproj /p:RestoreIgnoreFailedSources=true
dotnet test dome/tests/Dome.Tests/Dome.Tests.csproj /p:RestoreIgnoreFailedSources=true
```

**Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~DomeCliApplicationExecutorTests|FullyQualifiedName~TestSuiteLayoutAudit" /p:RestoreIgnoreFailedSources=true
dotnet sln D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln list
```

Expected: tests `PASS`, and the solution/project layout matches the docs.

**Step 5: Commit**

```bash
git add dome/.github/workflows/dome-ci.yml dome/docs dome/tests
git commit -m "docs: align ci and documentation with core cpg rewrite"
```

## Task 6: Full Regression Cutover and Delete the Old Analysis Spine

**Files:**
- Modify or delete: all remaining files that keep the old analysis spine alive after the CPG projection is proven stable
- Verify: `dome/dome.sln`
- Verify: `dome/src/Core/CPG/Tests/JoernishCpg.Tests.csproj`
- Verify: `dome/tests/Dome.Tests/Dome.Tests.csproj`

**Step 1: Write the failing test**

Add one final regression that proves the shipping solution can complete the main scenarios without any reference to the removed spine:

```csharp
[Fact]
public async Task SolutionCutover_ShouldPassAnalyzePlanRunAndShadowFlows()
{
    // Execute analyze-only, plan-only, standard run, runtime host, and shadow extraction
    // against sample inputs and assert success plus artifact generation.
}
```

**Step 2: Run test to verify it fails**

Run:

```powershell
dotnet build D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln /p:RestoreIgnoreFailedSources=true
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln /p:RestoreIgnoreFailedSources=true
```

Expected: `FAIL` until all old references are removed and every host flows through the new core.

**Step 3: Write minimal implementation**

Only after Tasks 1-5 are green:

1. Delete the no-longer-used legacy projects and source files.
2. Remove stale project references from the solution and project files.
3. Ensure every host resolves analysis through the CPG-backed adapter path.
4. Ensure sample-driven integration tests cover `analyze`, `plan`, `run`, `tr-run`, and shadow extraction.

**Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\CPG\Tests\JoernishCpg.Tests.csproj /p:RestoreIgnoreFailedSources=true
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj /p:RestoreIgnoreFailedSources=true
dotnet build D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln /p:RestoreIgnoreFailedSources=true
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln /p:RestoreIgnoreFailedSources=true
```

Expected: all commands `PASS`.

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: complete cpg-centered dome rewrite"
```

## Exit Criteria

The rewrite is only done when all of the following are true:

1. `src/Core/CPG` is part of `dome.sln` and no longer carries prototype naming or prototype paths.
2. `RoslynAnalysisEngine` builds its output from a CPG projection rather than the current in-file `BuildContext`.
3. The standard host, runtime host, and shadow-extraction host all run through the same CPG-backed analysis spine.
4. The old duplicate analysis spine is deleted or fully disconnected.
5. CPG tests, application tests, and solution build/test all pass.

## Risks to Watch Explicitly

1. Do not break the external `AnalysisOutput` shape faster than rules/planning/rewrite can absorb.
2. Do not duplicate source-of-truth logic between CPG projection and legacy `BuildContext`; once Task 3 lands, the old path must only exist as a temporary fallback, then be deleted.
3. Do not leave docs, tests, or CI pointing to `dome/prototypes/JoernishCpg`; the current `96/98` state proves path drift is real.
4. Do not attempt a flag-day host rewrite before the CPG package identity and CPG projection are both green.
