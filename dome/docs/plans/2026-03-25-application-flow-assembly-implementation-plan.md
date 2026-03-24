# Application Flow Assembly Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace hard-coded application stage arrays with a code-assembled fixed-slot flow model that supports standard contracts, `Replace<Slot>`, and `Decorate<Slot>` without introducing a fat business context.

**Architecture:** Keep the existing execution kernel in `src/Application/Pipeline`, add a shared flow-assembly layer there, expose stable slot contracts from `src/Application/Ports`, and migrate `Dome`, `Runtime`, and `ShadowExtraction` composition roots to recipe-based assembly. Preserve separate context types for the three application families, but move slot-to-slot business state transfer to immutable input/output records.

**Tech Stack:** C#, .NET 10, xUnit, existing `PipelineRunner<TContext>`, layered `apps/ + src/Application + src/Adapters + src/Core` solution layout.

---

## Preconditions

Read and treat this design as the source of truth before implementation:

- [`2026-03-24-application-flow-assembly-design.md`](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\docs\plans\2026-03-24-application-flow-assembly-design.md)

Execution must preserve:

- the standard Dome host
- the Terraria runtime host
- the shadow extraction host
- current whole-suite behavior

Execution must not:

- collapse all flows into one shared business context
- introduce JSON/config-defined flows
- expose arbitrary stage insertion on the public assembly surface

## Global Verification Commands

Use these environment variables for all `dotnet` commands:

```powershell
$env:DOTNET_CLI_HOME='D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\.dotnet-cli-home'
$env:HOME='D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\.dotnet-home'
```

Primary verification commands:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj -v minimal
dotnet build D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln -v minimal
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln --no-build -v minimal
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\CPG\Tests\JoernishCpg.Tests.csproj -v minimal
```

Expected steady-state result:

- `Dome.Tests`: `244/244` passing or higher if new tests are added
- `dome.sln` build: `0` errors
- `dome.sln` test: pass
- `JoernishCpg.Tests`: `98/98` passing or higher if new tests are added

Known non-blocking warnings:

- `NETSDK1057`
- `MSB3277`
- `NU1900`

## Task 1: Add Shared Flow Assembly Primitives

**Files:**
- Create: `dome/src/Application/Pipeline/FlowExecutionContext.cs`
- Create: `dome/src/Application/Pipeline/FlowRecipe.cs`
- Create: `dome/src/Application/Pipeline/FlowBuilder.cs`
- Modify: `dome/src/Application/Pipeline/PipelineAbstractions.cs`
- Test: `dome/tests/Dome.Tests/Application/Unit/FlowAssemblyKernelTests.cs`
- Test: `dome/tests/Dome.Tests/Application/Unit/PipelineRunnerTests.cs`

**Step 1: Write the failing kernel tests**

Create `FlowAssemblyKernelTests.cs` with tests covering:

```csharp
[Fact]
public void Build_DuplicateSlotReplacement_Throws() { }

[Fact]
public void Build_Decorators_AreAppliedInRegistrationOrder() { }

[Fact]
public void Build_MissingRequiredSlot_Throws() { }
```

Update `PipelineRunnerTests.cs` only if needed to pin current behavior for:

- ordered execution
- terminal short-circuit
- stage trace recording

**Step 2: Run the new kernel tests and verify failure**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~FlowAssemblyKernelTests" -v minimal
```

Expected:

- FAIL because `FlowExecutionContext`, `FlowRecipe`, or `FlowBuilder` do not exist yet

**Step 3: Implement the minimal shared primitives**

Add:

- a thin `FlowExecutionContext`
- a recipe model that holds selected slots and decorators
- a builder that validates:
  - required slots
  - at most one active implementation per slot
  - deterministic decorator ordering

Minimal shape:

```csharp
public sealed class FlowExecutionContext
{
    public string CorrelationId { get; }
    public IList<PipelineStageTrace> StageTraces { get; }
    public IDictionary<string, object?> Items { get; }
}
```

```csharp
public sealed class FlowBuilder<TContext>
{
    public IReadOnlyList<IPipelineStage<TContext>> Build() { ... }
}
```

**Step 4: Re-run the targeted tests**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~FlowAssemblyKernelTests|FullyQualifiedName~PipelineRunnerTests" -v minimal
```

Expected:

- PASS for the new kernel tests
- PASS for the existing runner tests

**Step 5: Commit**

```powershell
git add dome/src/Application/Pipeline/FlowExecutionContext.cs dome/src/Application/Pipeline/FlowRecipe.cs dome/src/Application/Pipeline/FlowBuilder.cs dome/src/Application/Pipeline/PipelineAbstractions.cs dome/tests/Dome.Tests/Application/Unit/FlowAssemblyKernelTests.cs dome/tests/Dome.Tests/Application/Unit/PipelineRunnerTests.cs
git commit -m "feat: add shared flow assembly primitives"
```

## Task 2: Add Stable Public Slot Contracts and Immutable I/O Records

**Files:**
- Create: `dome/src/Application/Ports/FlowAssemblyContracts.cs`
- Create: `dome/src/Application/Ports/DomeFlowContracts.cs`
- Modify: `dome/src/Application/Ports/Dome.Application.Abstractions.csproj`
- Modify: `dome/tests/Dome.Tests/Application/Contracts/PublicContractBoundaryTests.cs`
- Test: `dome/tests/Dome.Tests/Application/Unit/FlowSlotContractTests.cs`

**Step 1: Write the failing public-contract tests**

Create `FlowSlotContractTests.cs` to pin:

- each slot contract exists
- each slot takes immutable typed input
- each slot returns immutable typed output

Add contract-boundary checks in `PublicContractBoundaryTests.cs` to assert:

- standard slot contracts live in `TerrariaTools.Dome.Application.Ports`
- public assembly surface does not expose arbitrary `AddStage`-style APIs

Example assertions:

```csharp
Assert.NotNull(typeof(IAnalyzeSlot));
Assert.Equal(typeof(AnalyzeInput), parameter.ParameterType);
Assert.Equal(typeof(AnalyzeOutput), method.ReturnType);
```

**Step 2: Run the contract tests and verify failure**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~FlowSlotContractTests|FullyQualifiedName~PublicContractBoundaryTests" -v minimal
```

Expected:

- FAIL because the new contracts and records do not exist yet

**Step 3: Add the public slot contracts**

Implement stable Dome-family contracts:

```csharp
public interface ILoadSlot { ... }
public interface IAnalyzeSlot { ... }
public interface IRuleSlot { ... }
public interface IDecisionSlot { ... }
public interface IResultSlot { ... }
```

Implement immutable records:

```csharp
public sealed record LoadInput(...);
public sealed record LoadOutput(...);
public sealed record AnalyzeInput(...);
public sealed record AnalyzeOutput(...);
public sealed record RuleInput(...);
public sealed record RuleOutput(...);
public sealed record DecisionInput(...);
public sealed record DecisionOutput(...);
public sealed record ResultInput(...);
```

Do not add mutable builder-style inputs.

**Step 4: Re-run the targeted tests**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~FlowSlotContractTests|FullyQualifiedName~PublicContractBoundaryTests" -v minimal
```

Expected:

- PASS for the new contract tests
- PASS for existing contract boundary tests

**Step 5: Commit**

```powershell
git add dome/src/Application/Ports/FlowAssemblyContracts.cs dome/src/Application/Ports/DomeFlowContracts.cs dome/src/Application/Ports/Dome.Application.Abstractions.csproj dome/tests/Dome.Tests/Application/Contracts/PublicContractBoundaryTests.cs dome/tests/Dome.Tests/Application/Unit/FlowSlotContractTests.cs
git commit -m "feat: add stable application flow slot contracts"
```

## Task 3: Introduce Dome Recipes and Slot Adapters

**Files:**
- Create: `dome/apps/Dome.Application/Composition/DomeFlowRecipes.cs`
- Create: `dome/apps/Dome.Application/Composition/DomeSlotAdapters.cs`
- Modify: `dome/apps/Dome.Application/Composition/DomeApplicationComposition.cs`
- Modify: `dome/src/Application/Pipeline/DomeApplicationStages.cs`
- Modify: `dome/tests/Dome.Tests/Application/Unit/DomeApplicationPipelineTests.cs`
- Modify: `dome/tests/Dome.Tests/Application/Unit/DomeApplicationOrchestrationTests.cs`
- Test: `dome/tests/Dome.Tests/Application/Unit/DomeFlowRecipeTests.cs`

**Step 1: Write the failing Dome recipe tests**

Create `DomeFlowRecipeTests.cs` covering:

- `AnalyzeOnly` assembles `Load -> Analyze -> Result`
- `PlanOnly` assembles `Load -> Analyze -> Rule -> Decision -> Result`
- `Standard` assembles the full production path
- `ReplaceAnalyze` swaps only the analyze slot
- `DecorateResult` wraps only the result slot

Minimal assertions:

```csharp
Assert.Equal(["Load", "Analyze", "Result"], stageNames);
Assert.IsType<SpecialAnalyzeSlotAdapter>(recipe.Analyze);
```

**Step 2: Run the Dome recipe tests and verify failure**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~DomeFlowRecipeTests" -v minimal
```

Expected:

- FAIL because the recipe and slot adapter files do not exist yet

**Step 3: Add recipe objects and adapters**

Implement:

- one recipe file for the Dome family
- one adapter layer that wraps the current load/analyze/rule/decision/result logic behind the new slot contracts

Mapping:

- `WorkspaceLoadStage` logic -> `ILoadSlot`
- `AnalysisStage` logic -> `IAnalyzeSlot`
- `MarkDecisionsStage` logic -> `IRuleSlot`
- `CompilePlanStage` logic -> `IDecisionSlot`
- finalize/rewrite logic -> `IResultSlot`

Keep existing stage classes intact until the recipe layer is green.

**Step 4: Switch Dome composition to recipe-based assembly**

Replace the manual `new ...Stage(...)` list in `DomeApplicationComposition.cs` with:

- recipe selection
- slot implementation binding
- flow builder invocation

`DomeApplication` itself should continue delegating to `IPipelineRunner<DomePipelineContext>` so host behavior stays stable.

**Step 5: Re-run the Dome tests**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~DomeFlowRecipeTests|FullyQualifiedName~DomeApplicationPipelineTests|FullyQualifiedName~DomeApplicationOrchestrationTests" -v minimal
```

Expected:

- PASS for the new recipe tests
- PASS for existing Dome pipeline and orchestration tests

**Step 6: Commit**

```powershell
git add dome/apps/Dome.Application/Composition/DomeFlowRecipes.cs dome/apps/Dome.Application/Composition/DomeSlotAdapters.cs dome/apps/Dome.Application/Composition/DomeApplicationComposition.cs dome/src/Application/Pipeline/DomeApplicationStages.cs dome/tests/Dome.Tests/Application/Unit/DomeFlowRecipeTests.cs dome/tests/Dome.Tests/Application/Unit/DomeApplicationPipelineTests.cs dome/tests/Dome.Tests/Application/Unit/DomeApplicationOrchestrationTests.cs
git commit -m "feat: assemble Dome application flows from fixed-slot recipes"
```

## Task 4: Add Runtime Family Recipes and Convert Runtime Composition

**Files:**
- Create: `dome/apps/Dome.Application.Runtime/Composition/TerrariaRuntimeFlowRecipes.cs`
- Create: `dome/apps/Dome.Application.Runtime/Composition/TerrariaRuntimeSlotAdapters.cs`
- Modify: `dome/apps/Dome.Application.Runtime/Composition/TerrariaRuntimeComposition.cs`
- Modify: `dome/src/Application/UseCases/Runtime/TerrariaRuntimeApplicationStages.cs`
- Modify: `dome/tests/Dome.Tests/Application/Unit/TerrariaRuntimeApplicationOrchestrationTests.cs`
- Test: `dome/tests/Dome.Tests/Application/Unit/TerrariaRuntimeFlowRecipeTests.cs`

**Step 1: Write the failing runtime recipe tests**

Add tests covering:

- runtime recipe order is stable
- runtime recipe keeps separate runtime context
- runtime recipe still delegates into the standard Dome application runner
- runtime result persistence remains the last step

**Step 2: Run the runtime recipe tests and verify failure**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~TerrariaRuntimeFlowRecipeTests" -v minimal
```

Expected:

- FAIL because the runtime recipe layer does not exist yet

**Step 3: Implement runtime recipes and slot adapters**

Create runtime-family slots with runtime-specific vocabulary. Do not force the Dome slot names onto the runtime family.

Recommended runtime slot language:

- `Prepare`
- `ExecuteDome`
- `LoadReport`
- `BuildWorkspace`
- `Persist`

**Step 4: Switch runtime composition root**

Replace the manual stage list in `TerrariaRuntimeComposition.cs` with recipe-based assembly.

Keep:

- `TerrariaRuntimePipelineContext`
- current host behavior
- current progress reporting and report store wiring

**Step 5: Re-run runtime tests**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~TerrariaRuntimeFlowRecipeTests|FullyQualifiedName~TerrariaRuntimeApplicationOrchestrationTests" -v minimal
```

Expected:

- PASS for runtime recipe tests
- PASS for existing runtime orchestration tests

**Step 6: Commit**

```powershell
git add dome/apps/Dome.Application.Runtime/Composition/TerrariaRuntimeFlowRecipes.cs dome/apps/Dome.Application.Runtime/Composition/TerrariaRuntimeSlotAdapters.cs dome/apps/Dome.Application.Runtime/Composition/TerrariaRuntimeComposition.cs dome/src/Application/UseCases/Runtime/TerrariaRuntimeApplicationStages.cs dome/tests/Dome.Tests/Application/Unit/TerrariaRuntimeFlowRecipeTests.cs dome/tests/Dome.Tests/Application/Unit/TerrariaRuntimeApplicationOrchestrationTests.cs
git commit -m "feat: assemble runtime application flows from recipes"
```

## Task 5: Add Shadow Extraction Family Recipes and Convert Shadow Composition

**Files:**
- Create: `dome/apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionFlowRecipes.cs`
- Create: `dome/apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionSlotAdapters.cs`
- Modify: `dome/apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionComposition.cs`
- Modify: `dome/src/Application/UseCases/ShadowExtraction/TerrariaRuntimeShadowExtractionPipelineStages.cs`
- Modify: `dome/tests/Dome.Tests/Application/Unit/TerrariaRuntimeShadowExtractionApplicationOrchestrationTests.cs`
- Test: `dome/tests/Dome.Tests/Application/Unit/TerrariaRuntimeShadowExtractionFlowRecipeTests.cs`

**Step 1: Write the failing shadow recipe tests**

Add tests covering:

- stable shadow recipe order
- shadow recipe keeps the shadow-specific context
- closure planning remains before workspace write
- build and report persistence remain terminal steps

**Step 2: Run the shadow recipe tests and verify failure**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~TerrariaRuntimeShadowExtractionFlowRecipeTests" -v minimal
```

Expected:

- FAIL because the shadow recipe layer does not exist yet

**Step 3: Implement shadow recipes and slot adapters**

Recommended shadow slot language:

- `ResolveInput`
- `Analyze`
- `BuildClosure`
- `WriteWorkspace`
- `Build`
- `Persist`

Preserve existing shadow closure and workspace semantics.

**Step 4: Switch shadow composition root**

Replace the manual stage list in `TerrariaRuntimeShadowExtractionComposition.cs` with recipe-based assembly.

Keep:

- `ShadowExtractionPipelineContext`
- current report builder/store behavior
- current build executor behavior

**Step 5: Re-run shadow tests**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~TerrariaRuntimeShadowExtractionFlowRecipeTests|FullyQualifiedName~TerrariaRuntimeShadowExtractionApplicationOrchestrationTests" -v minimal
```

Expected:

- PASS for shadow recipe tests
- PASS for existing shadow orchestration tests

**Step 6: Commit**

```powershell
git add dome/apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionFlowRecipes.cs dome/apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionSlotAdapters.cs dome/apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionComposition.cs dome/src/Application/UseCases/ShadowExtraction/TerrariaRuntimeShadowExtractionPipelineStages.cs dome/tests/Dome.Tests/Application/Unit/TerrariaRuntimeShadowExtractionFlowRecipeTests.cs dome/tests/Dome.Tests/Application/Unit/TerrariaRuntimeShadowExtractionApplicationOrchestrationTests.cs
git commit -m "feat: assemble shadow extraction flows from recipes"
```

## Task 6: Add Replace and Decorate Integration Coverage

**Files:**
- Modify: `dome/tests/Dome.Tests/Application/Unit/FlowAssemblyKernelTests.cs`
- Modify: `dome/tests/Dome.Tests/Application/Unit/DomeFlowRecipeTests.cs`
- Modify: `dome/tests/Dome.Tests/Application/Contracts/PublicContractBoundaryTests.cs`
- Test: `dome/tests/Dome.Tests/Application/Integration/SolutionCutoverTests.cs`

**Step 1: Write the failing integration tests**

Add tests for:

- replacing the analyze slot does not affect rule/decision/result public contracts
- decorating the result slot does not change flow topology
- adapters can hide implementation-specific request/response models behind the stable public slot contracts

**Step 2: Run the focused tests and verify failure**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~FlowAssemblyKernelTests|FullyQualifiedName~DomeFlowRecipeTests|FullyQualifiedName~SolutionCutoverTests|FullyQualifiedName~PublicContractBoundaryTests" -v minimal
```

Expected:

- FAIL until replacement/decorator guardrails are fully implemented

**Step 3: Tighten builder and contract validation**

Implement the missing validation if needed:

- one active implementation per slot
- decorators may not change public slot types
- adapters must surface standard slot contracts only
- no public arbitrary insertion API appears in ports or composition code

**Step 4: Re-run the focused tests**

Run the same command from Step 2.

Expected:

- PASS for all focused replacement/decorator tests

**Step 5: Commit**

```powershell
git add dome/tests/Dome.Tests/Application/Unit/FlowAssemblyKernelTests.cs dome/tests/Dome.Tests/Application/Unit/DomeFlowRecipeTests.cs dome/tests/Dome.Tests/Application/Contracts/PublicContractBoundaryTests.cs dome/tests/Dome.Tests/Application/Integration/SolutionCutoverTests.cs
git commit -m "test: cover replace and decorate flow assembly behavior"
```

## Task 7: Remove Manual Assembly Duplication and Clean Up Internal Names

**Files:**
- Modify: `dome/apps/Dome.Application/Composition/DomeApplicationComposition.cs`
- Modify: `dome/apps/Dome.Application.Runtime/Composition/TerrariaRuntimeComposition.cs`
- Modify: `dome/apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionComposition.cs`
- Modify: `dome/src/Application/Pipeline/PipelineAbstractions.cs`
- Modify: `dome/tests/TerrariaTools.Testing/Assertions/TestSuiteLayoutAuditor.cs`
- Modify: `dome/docs/architecture/overview.md`
- Modify: `dome/docs/architecture/project-layout.md`

**Step 1: Write or update any failing audit tests**

If naming/layout changes require audit updates, first add the failing assertions to the relevant test or audit file.

**Step 2: Remove leftover hand-built stage arrays**

Delete or narrow leftover manual stage array construction so that:

- composition roots select recipes
- builders assemble flows
- the old manual assembly pattern no longer remains the default path

**Step 3: Re-run the audit and unit checks**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj --filter "FullyQualifiedName~TestSuiteLayoutAuditTests|FullyQualifiedName~DomeApplicationPipelineTests|FullyQualifiedName~TerrariaRuntimeApplicationOrchestrationTests|FullyQualifiedName~TerrariaRuntimeShadowExtractionApplicationOrchestrationTests" -v minimal
```

Expected:

- PASS for audit and orchestration tests

**Step 4: Update architecture docs**

Reflect the new recipe-based assembly model in:

- `overview.md`
- `project-layout.md`

Keep the docs concise and aligned with the implementation.

**Step 5: Commit**

```powershell
git add dome/apps/Dome.Application/Composition/DomeApplicationComposition.cs dome/apps/Dome.Application.Runtime/Composition/TerrariaRuntimeComposition.cs dome/apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionComposition.cs dome/src/Application/Pipeline/PipelineAbstractions.cs dome/tests/TerrariaTools.Testing/Assertions/TestSuiteLayoutAuditor.cs dome/docs/architecture/overview.md dome/docs/architecture/project-layout.md
git commit -m "refactor: remove manual application flow assembly"
```

## Task 8: Whole-Suite Verification and Final Regression Pass

**Files:**
- Verify only

**Step 1: Run the full Dome test suite**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\tests\Dome.Tests\Dome.Tests.csproj -v minimal
```

Expected:

- PASS

**Step 2: Run the full solution build**

Run:

```powershell
dotnet build D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln -v minimal
```

Expected:

- PASS
- warnings only from the known non-blocking set

**Step 3: Run the full solution test pass**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\dome.sln --no-build -v minimal
```

Expected:

- PASS

**Step 4: Re-run the CPG suite**

Run:

```powershell
dotnet test D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\CPG\Tests\JoernishCpg.Tests.csproj -v minimal
```

Expected:

- PASS

**Step 5: Commit**

```powershell
git add -A
git commit -m "test: verify application flow assembly migration"
```

## Notes for Execution

- Keep each task small and TDD-first.
- Do not delete the current pipeline classes before the recipe-based path is green.
- Do not unify business contexts across Dome, runtime, and shadow extraction.
- Do not expose arbitrary insertion APIs publicly.
- Do not allow implementation-specific request/response models to leak past slot adapters.

## Done Criteria

The work is complete only when all of the following are true:

1. Standard Dome composition uses recipe-based fixed-slot flow assembly.
2. Runtime composition uses recipe-based fixed-slot flow assembly.
3. Shadow extraction composition uses recipe-based fixed-slot flow assembly.
4. `Replace<Slot>` works for supported slots without changing public contracts.
5. `Decorate<Slot>` works without mutating topology.
6. Slot-to-slot business transfer uses typed immutable input/output models.
7. No new fat business context is introduced.
8. Full build and test verification passes.
