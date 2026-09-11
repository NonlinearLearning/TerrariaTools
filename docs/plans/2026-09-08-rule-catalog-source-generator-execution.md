# IRuleDefinition Compile-Time Rule Catalog Implementation Plan

> **执行状态（2026-09-09）：** 已完成实现和核心验收。本文保留 Task 0 至 Task 7 的执行顺序作为可追溯计划，并在末尾记录实际路径、测试证据和剩余边界。

> **执行前置：** 开始前先读取仓库级 `Context/AGENTS.md`、`Context/progress.md`、`Context/feature_list.json` 和 `docs/harness-runtime.md`；按下文 Task 0 至 Task 7 顺序执行，并在每个 checkpoint 保存可回滚证据。

**Goal:** Replace the hand-maintained rule construction lists formerly owned by `RuleRegistry` with a Roslyn incremental source-generated catalog and `RulePipelineComposer` while preserving feature gates, disabled-rule declarations, ordering, rule identities, graph shape, and analysis output.

**Architecture:** `NLISSN.Rule.Generator` attached to `NLISSN.Rules` discovers concrete Mark, Propagate, Lift, and Propose rule types in the current compilation. It requires explicit feature metadata, validates constructibility, reports compiler diagnostics, and emits direct static factories. `RulePipelineComposer` consumes the generated four-stage catalog, performs feature and disabled-rule selection, `RuleCatalog.ValidateRules`, stable ordering, and `RulePipeline` assembly. The old `RuleRegistry` composition root has been removed.

**Tech Stack:** .NET SDK 10.0.200-preview.0.26103.119 from global.json, C# preview, Roslyn IIncrementalGenerator, CSharpGeneratorDriver, xUnit, the existing NLISSN Host/Contract projects, and the repository serial-build harness.

---

## Execution boundary

This plan was written before implementation. The target architecture is frozen in [规则目录：编译期生成](../../设计docs/目前设计/规则目录-编译期生成.md); the execution record below is the authoritative as-built update.

Do not begin by deleting the manual lists. First establish a complete implementation-vs-registration contract and freeze the current rule-set baselines. If that contract fails, stop and repair the existing registry or rule inventory before introducing a generator.

## Non-negotiable invariants

The implementation is not accepted if any of these change without a separately approved feature:

- Default rule membership and four-stage order.
- All-feature rule membership and four-stage order.
- RuleId, Consumes, Produces, and RuleInputCardinality.
- RuleNodeId values and compiled rule-graph declarations.
- legacy `disabledRuleTypes` case-insensitive mapping behavior.
- Disabled rule declarations retained in RulePipeline and the compiled graph.
- RuleCatalog.ValidateRules timing and candidate set.
- Required CPG capability aggregation.
- DOP 1, 2, and 16 stage snapshots, evidence, decisions, rewritten source, and per-file diff bytes.
- No runtime Assembly.GetTypes, Activator.CreateInstance, DependencyContext, or plugin probing in the generated/runtime path.

## Change map

| Area | Planned files | Purpose |
|---|---|---|
| Registration contract | src/NLISSN.Rule/RuleRegistration.cs; src/NLISSN.Rule/RuleRegistrationDescriptor.cs | Feature metadata and the small generated-directory interface |
| Generator | src/NLISSN.Rule.Generator/NLISSN.Rule.Generator.csproj; src/NLISSN.Rule.Generator/*.cs | Symbol discovery, diagnostics, and direct factory emission |
| Generator attachment | src/NLISSN.Rules/NLISSN.Rules.csproj | Attach the generator as an analyzer without a runtime reference edge |
| Runtime composition | src/NLISSN/Composition/RulePipelineComposer.cs; src/NLISSN/Composition/RuleSelection.cs; src/NLISSN/Composition/RuleSelectionAdapter.cs | Consume generated descriptors and adapt legacy settings |
| Rule metadata | Concrete files under src/NLISSN.Rules/Mark, Propagate, Lift, and Propose | Declare Core or one existing optional feature |
| Composition | src/NLISSN/Composition/RulePipelineComposer.cs | Consume generated descriptors while preserving filtering, sorting, and disabled declarations |
| Generator test wiring | tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj | Reference the generator assembly and the Roslyn driver API from tests |
| Generator tests | tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogGeneratorTests.cs | Roslyn driver tests for output and diagnostics |
| Runtime contracts | tests/NLISSN.ContractTests/RuleCatalog/*.cs; tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBaselineTests.cs | Exact generated-directory coverage and behavior equivalence |
| Documentation | this plan and 设计docs/目前设计/规则目录-编译期生成.md | Keep execution and architecture aligned |

The exact list of rule metadata edits must be generated from the source inventory during Task 0. Do not hand-write an incomplete list from memory.

## Task 0: Establish the baseline and completeness contract

Files:

- Create tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBaselineTests.cs.
- Create tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBuildContractTests.cs.
- Read src/NLISSN/Composition/RulePipelineComposer.cs and the four RuleDefinition stage bases.

### Step 0.1: Preserve the dirty-worktree boundary

Run:

~~~powershell
git status --short
pwsh -File .\Miscellaneous\init.ps1
~~~

Expected:

- Existing user changes are recorded and not reverted.
- The NLISSN health build passes.
- If init reports a known restore blocker, record it and do not claim end-to-end verification.

### Step 0.2: Inventory concrete rules

Run:

~~~powershell
rg -n "public sealed class .*Rule" .\src\NLISSN.Rules
rg -n "public abstract class .*Rule|public class .*Rule" .\src\NLISSN.Rules
~~~

Classify every concrete type by the first matching base family:

~~~text
RuleDefinitionMark       -> Mark
RuleDefinitionPropagate  -> Propagate
RuleDefinitionLift       -> Lift
RuleDefinitionPropose    -> Propose
~~~

Record the fully qualified type name and simple type name. The simple name remains part of the legacy `disabledRuleTypes` adapter contract, while runtime composition uses RuleId.

### Step 0.3: Write the completeness test

The test may use reflection as a test-side inventory mechanism. It must not become production registration logic. Its core comparison should be equivalent to:

~~~csharp
var ruleAssembly = typeof(AtomicIdentifierNameMarkRule).Assembly;

var implementations = ruleAssembly
    .GetTypes()
    .Where(type =>
        !type.IsAbstract &&
        typeof(IRuleDefinition).IsAssignableFrom(type))
    .ToHashSet();

var pipeline = RulePipelineComposer.Compose(new RuleSelection(new[]
{
    RuleFeature.UnreachableMethodDeletion,
    RuleFeature.UnreferencedMethodDeletion,
    RuleFeature.UnusedInterfaceImplementationCleanup,
    RuleFeature.InternalOnlyPublicMethodPrivatization,
})).Pipeline;

var registered = pipeline.Markers.Cast<IRuleDefinition>()
    .Concat(pipeline.Propagators)
    .Concat(pipeline.Lifters)
    .Concat(pipeline.Proposers)
    .Select(rule => rule.GetType())
    .ToHashSet();
~~~

Assert exact set equality and exact per-stage equality. Also assert the default pipeline against a frozen default snapshot and the all-feature pipeline against a frozen all-feature snapshot. Counts alone are insufficient.

### Step 0.4: Run the baseline

Run:

~~~powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleCatalogBaselineTests|FullyQualifiedName~RuleCatalogBuildContractTests"
~~~

Expected: the generated catalog matches the frozen default and all-feature snapshots. If it fails, stop and inspect the registration inventory.

### Step 0.5: Checkpoint

Create a focused checkpoint, without unrelated worktree files:

~~~text
test: freeze rule catalog completeness baseline
~~~

## Task 1: Add the compile-time registration contract

Files:

- Create src/NLISSN.Rule/RuleRegistration.cs.
- Create src/NLISSN.Rule/RuleRegistrationDescriptor.cs.
- Create tests/NLISSN.ContractTests/RuleCatalog/RuleRegistrationContractTests.cs.
- Read src/NLISSN.Rule/RuleGraph.cs.

The existing RuleKind in RuleGraph.cs already represents Mark, Propagate, Lift, and Propose. Reuse it as the descriptor Kind/stage; do not add a duplicate enum.

### Step 1.1: Define feature metadata

The contract should be equivalent to:

~~~csharp
namespace NLISSN.Core.Pipeline;

public enum RuleFeature
{
    Core,
    UnreachableMethodDeletion,
    UnreferencedMethodDeletion,
    UnusedInterfaceImplementationCleanup,
    InternalOnlyPublicMethodPrivatization
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuleRegistrationAttribute : Attribute
{
    public RuleRegistrationAttribute(RuleFeature feature)
    {
        Feature = feature;
    }

    public RuleFeature Feature { get; }
}
~~~

Do not add feature or default-enable properties to IRuleDefinition. Feature is composition metadata; IRuleDefinition remains the execution contract.

### Step 1.2: Define the descriptor

Use the small stage-generic runtime contract with no Roslyn types:

~~~csharp
public sealed record RuleRegistration<TStage>(
    string TypeName,
    string FullyQualifiedName,
    RuleFeature Feature,
    Func<TStage> Factory)
    where TStage : class, IRuleDefinition;
~~~

The stage-generic descriptor keeps the generated catalog strongly typed. `RuleId` is the canonical runtime identity; `TypeName` and `FullyQualifiedName` support deterministic ordering and legacy-name adaptation. Feature comes from the attribute. Factory is generated code.

### Step 1.3: Test the contract

Cover:

- RuleRegistrationAttribute cannot be applied twice;
- all current feature switches have stable enum values;
- the descriptor contains no Compilation, ISymbol, syntax-node, or analyzer state;
- RuleKind remains the only Stage identity used by the directory.

Run:

~~~powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleRegistrationContractTests"
~~~

Expected: all contract tests pass before the generator is attached.

## Task 2: Create and attach the generator

Files:

- Create src/NLISSN.Rule.Generator/NLISSN.Rule.Generator.csproj.
- Create src/NLISSN.Rule.Generator/RuleCatalogGenerator.cs.
- Create src/NLISSN.Rule.Generator/RuleCatalogDiagnostics.cs.
- Modify src/NLISSN.Rules/NLISSN.Rules.csproj.
- Modify tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj.
- Create tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogGeneratorTests.cs.

### Step 2.1: Create the analyzer project

Use these properties:

~~~xml
<TargetFramework>netstandard2.0</TargetFramework>
<LangVersion>preview</LangVersion>
<IncludeBuildOutput>false</IncludeBuildOutput>
<IsPackable>false</IsPackable>
~~~

Add private compiler/analyzer package references compatible with the repository, initially Microsoft.CodeAnalysis.CSharp 4.14.0 and Microsoft.CodeAnalysis.Analyzers when needed. The generator project must not reference NLISSN.Rules, NLISSN.Application, or the executable.

Add an ordinary project reference from the Contract test project to the generator project so tests can instantiate the generator type. Add an explicit Microsoft.CodeAnalysis.CSharp 4.14.0 package reference to the Contract test project if the driver types are not already available through its existing graph. Do not attach the generator project to the Contract tests as an analyzer; the tests need its public generator implementation as a test dependency.

### Step 2.2: Attach it without a runtime edge

Add to src/NLISSN.Rules/NLISSN.Rules.csproj:

~~~xml
<ProjectReference
  Include="..\NLISSN.Rule.Generator\NLISSN.Rule.Generator.csproj"
  OutputItemType="Analyzer"
  ReferenceOutputAssembly="false" />
~~~

The generated file is compiled into NLISSN.Rules. The generator assembly is analyzer-only. Confirm:

~~~text
NLISSN.Rules -> generator as analyzer
NLISSN.Rules -> NLISSN.Core
generator -/-> NLISSN.Rules runtime assembly
~~~

### Step 2.3: Add the first red generator test

Use CSharpGeneratorDriver with an in-memory source containing one public concrete RuleDefinitionMark subclass, a Core registration attribute, a public parameterless constructor, and a minimal RuleId override.

Assert that the generated catalog contains the type. Before the generator is implemented this test must fail because the catalog source is absent. This is the intentional red state.

Run:

~~~powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleCatalogGeneratorTests"
~~~

## Task 3: Implement discovery, diagnostics, and deterministic emission

Files:

- Modify src/NLISSN.Rule.Generator/RuleCatalogGenerator.cs.
- Modify src/NLISSN.Rule.Generator/RuleCatalogDiagnostics.cs.
- Create model/emitter files under src/NLISSN.Rule.Generator if needed.
- Modify tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogGeneratorTests.cs.

### Step 3.1: Discover all candidates, including missing metadata

Use IIncrementalGenerator. Start from class declarations, obtain INamedTypeSymbol, and inspect concrete non-abstract implementations of IRuleDefinition. Ignore ordinary abstract stage bases; an attributed abstract type is an invalid registration. For every concrete implementation, determine exactly one of the four legal stage bases and report Invalid stage when it belongs to none or cannot be classified uniquely. Do not collect only attributed classes: that would make an unannotated new rule invisible and recreate the original silent omission.

The semantic pipeline must:

1. Resolve IRuleDefinition and the four base symbols from the current compilation.
2. Walk every concrete implementation's base-type chain and determine exactly one RuleKind.
3. Read exactly one RuleRegistrationAttribute from each valid concrete stage rule.
4. Read the RuleFeature constructor argument.
5. Check public accessibility and a public parameterless constructor.
6. Build an immutable generator model.
7. Deduplicate partial declarations with SymbolEqualityComparer.Default.
8. Sort by fully qualified metadata name before emission.

### Step 3.2: Add stable diagnostics

Define stable IDs for at least:

| Category | Condition | Severity |
|---|---|---|
| Missing metadata | Concrete four-stage rule lacks RuleRegistrationAttribute | Warning; omitted from the catalog |
| Invalid stage | IRuleDefinition implementation is outside the four stage bases | Error |
| Invalid metadata | Duplicate attribute or unknown feature value | Error |
| Not constructible | No accessible parameterless constructor or inaccessible type | Error |
| Duplicate identity | Two rules expose the same RuleId | Error |
| Emission failure | Catalog cannot be produced completely | Error |

Diagnostics point to the rule class or registration attribute. On any Error, emit no partial usable catalog. A partial catalog would turn a compile-time error into runtime omission.

### Step 3.3: Emit direct factories

Generate a deterministic file named NLISSN.Rules.GeneratedRuleCatalog.g.cs with four strongly typed stage lists and direct factories. The generated type is:

~~~csharp
namespace NLISSN.Rules;

public static class GeneratedRuleCatalog
{
    public static IReadOnlyList<RuleRegistration<RuleDefinitionMark>> Markers { get; }
    public static IReadOnlyList<RuleRegistration<RuleDefinitionPropagate>> Propagators { get; }
    public static IReadOnlyList<RuleRegistration<RuleDefinitionLift>> Lifters { get; }
    public static IReadOnlyList<RuleRegistration<RuleDefinitionPropose>> Proposers { get; }
}
~~~

Use fully qualified global:: names in generated code. Do not emit reflection, service-provider lookup, type-name lookup, assembly scanning, or runtime code generation.

### Step 3.4: Turn generator tests green

Cover:

- one valid rule per stage;
- multiple partial declarations of one rule;
- missing metadata;
- unannotated abstract stage base is ignored, while an attributed abstract rule reports Not constructible;
- invalid stage;
- missing public parameterless constructor;
- inaccessible rule class;
- duplicate registration attribute;
- duplicate RuleId;
- stable generated text after declaration reordering;
- no usable catalog when an Error diagnostic exists.

Expected: valid cases generate one complete catalog; invalid cases emit the expected diagnostic and no usable catalog.

### Step 3.5: Checkpoint

Use a focused checkpoint:

~~~text
feat: generate static rule catalog with compile-time diagnostics
~~~

Do not annotate production rules or change RuleRegistry until generator-driver tests are green.

## Task 4: Annotate all concrete rules

Files:

- Modify every concrete rule file returned by rg -l "public sealed class .*Rule" src/NLISSN.Rules.
- Do not modify abstract rule bases only used for inheritance.
- Update generator tests only when the inventory assertion needs it.

### Step 4.1: Apply the exact feature mapping

| Concrete family | Metadata |
|---|---|
| All existing rules not listed below | RuleFeature.Core |
| UnreachableMethodMarkRule, UnreachableMethodPropagationRule, UnreachableMethodLiftingRule, and UnreachableMethodProposalRule | RuleFeature.UnreachableMethodDeletion |
| UnreferencedMethodMarkRule, UnreferencedMethodPropagationRule, UnreferencedMethodLiftingRule, and UnreferencedMethodProposalRule | RuleFeature.UnreferencedMethodDeletion |
| ClearUnusedInterfaceImplementationRule, its Propagation, Lifting, and Proposal rules | RuleFeature.UnusedInterfaceImplementationCleanup |
| PrivatizeInternalOnlyPublicMethodRule, its Propagation, Lifting, and Proposal rules | RuleFeature.InternalOnlyPublicMethodPrivatization |

The mapping must come from the existing RuleRegistry branches, not from guessed directory or class-name conventions.

### Step 4.2: Verify metadata coverage

Run:

~~~powershell
rg -n "public sealed class .*Rule" .\src\NLISSN.Rules
~~~

Then use a read-only loop over those files to report files that lack RuleRegistration. The missing-file set must be empty. The generator must still enforce this independently.

### Step 4.3: Build NLISSN.Rules

Run:

~~~powershell
dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
~~~

Expected: generated catalog compiles into NLISSN.Rules; there are no generator Error diagnostics and no runtime project-reference edge to the generator.

## Task 5: Add the runtime composer and remove the manual registry

Files:

- Create src/NLISSN/Composition/RulePipelineComposer.cs.
- Create src/NLISSN/Composition/RuleSelection.cs.
- Create src/NLISSN/Composition/RuleSelectionAdapter.cs.
- Delete src/NLISSN/Composition/RuleRegistry.cs.
- Modify tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBaselineTests.cs.
- Modify tests/NLISSN.HostTests/Application/RulePipelineComposerTests.cs.

### Step 5.1: Keep a small runtime seam

Create the composition entry point equivalent to:

~~~csharp
namespace NLISSN.Composition;

public static class RulePipelineComposer
{
    public static RuleCompositionResult Compose(RuleSelection selection) =>
        ...;
}
~~~

Roslyn symbols and generator models must not leak into Application or runtime rule execution. `RulePipelineComposer` is the only runtime composition root.

### Step 5.2: Replace manual construction

Implement this sequence inside `RulePipelineComposer.Compose`:

~~~text
normalize disabled names
select descriptors whose RuleFeature is enabled by `RuleSelection`
call every selected descriptor Factory, including rules that will be disabled by RuleId
validate all selected instances with RuleCatalog.ValidateRules
group stage instances by the four generated catalog properties
sort by TypeName and fully qualified name
construct the existing RulePipeline with active and disabled stage lists
~~~

Do not filter disabled descriptors before materialization; disabled rules are retained in the pipeline and graph declarations.

### Step 5.3: Preserve ordering

Generated descriptor order is only generation determinism. Runtime order must continue to use the current rule.GetType().Name ordinal sort. Do not substitute file order, source order, feature order, or metadata-name order.

### Step 5.4: Run focused runtime tests

Verify:

- every concrete implementation appears exactly once;
- stage membership is exact;
- default configuration matches the frozen default baseline;
- all features match the frozen all-feature baseline;
- disabled type names appear in the right disabled collection and graph declarations;
- feature-off rules are absent;
- duplicate capability validation still fails.

Run:

~~~powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RulePipelineComposerTests|FullyQualifiedName~RuleGraphCompilerTests|FullyQualifiedName~PipelineComponentTests"
~~~

Expected: all focused Host tests pass using the generated path.

### Step 5.5: Checkpoint

Use:

~~~text
refactor: compose rules from generated catalog
~~~

The manual implementation was removed after the generated-vs-baseline tests passed. Do not add a user-facing migration switch.

## Task 6: Prove graph and analysis behavior equivalence

Files:

- Modify tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBaselineTests.cs.
- Modify tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogGeneratorTests.cs.
- Read existing RuleGraph, evidence, rewrite, diff, and DOP tests identified with rg -n "DOP|Dop|evidence|rewrite|diff" tests/NLISSN.HostTests tests/NLISSN.ContractTests.

### Step 6.1: Compare exact snapshots

For default and all-feature configurations compare:

- fully qualified type lists;
- per-stage lists;
- RuleId lists;
- required capability set;
- active and disabled sets.

A count-only comparison is not sufficient.

### Step 6.2: Compare compiled graph declarations

Compare RuleNodeId, RuleKind, contract edges, active/disabled flags, and any declaration order promised by existing graph contracts. Reuse existing graph comparison helpers.

### Step 6.3: Run serial build and focused tiers

Run:

~~~powershell
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:BuildInParallel=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build --no-restore -p:UseSharedCompilation=false
~~~

If restore/build is blocked, record verificationStatus: not-run for the affected tier and do not claim equivalence.

### Step 6.4: Run the repository fast tier

Run:

~~~powershell
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast -Host
~~~

Record the exact current counts and date in Context/feature_list.json. Do not copy historical counts.

### Step 6.5: Run DOP 1, 2, and 16 comparisons

Use the repository's existing DOP entry points discovered in Step 6.1. Compare generated and frozen manual outputs for:

~~~text
stage snapshots
evidence
decisions
rewritten source
per-file diff bytes
required capabilities
compiled graph declarations
~~~

Stop on any difference, even if counts remain equal.

### Step 6.6: Checkpoint

Use:

~~~text
test: prove generated rule catalog behavior equivalence
~~~

## Task 7: Close documentation and repository state

Files:

- Remove the obsolete src/NLISSN/Composition/RuleRegistry.cs.
- Modify tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogBaselineTests.cs.
- Modify 设计docs/目前设计/规则目录-编译期生成.md.
- Modify Context/progress.md.
- Modify Context/feature_list.json.

### Step 7.1: Delete only duplicated registry data

Remove the four manual arrays and feature-specific append blocks. Keep `RuleSelection`, disabled-id normalization, feature selection, `RuleCatalog.ValidateRules`, active/disabled splitting, `RulePipeline` construction, and capability forwarding in `RulePipelineComposer`.

Do not move graph compilation into the generator or `RulePipelineComposer`.

### Step 7.2: Re-run all focused and full required checks

Run the Task 5 and Task 6 commands again. Results must remain identical after the manual arrays are deleted.

### Step 7.3: Update state documents

Only after implementation and tests pass:

- Change the design page from proposed/not implemented to source-aligned current design.
- Record generator path, facade seam, constructor boundary, diagnostic IDs, commands, exact results, and remaining limitations.
- Keep Context/progress.md concise: current feature, verified facts, blockers, and next step.
- Update the matching Context/feature_list.json entry with exact definition of done and evidence.

### Step 7.4: Final repository checks

Run:

~~~powershell
pwsh -File .\Miscellaneous\init.ps1
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
~~~

Also run the repository's changed-document relative-link check. If the harness still fails on the known physical Context/AGENTS.md expectation mismatch, report that failure separately; do not rewrite unrelated guidance just to satisfy a stale assertion.

## Execution record (2026-09-09)

The implementation reached the planned end state:

- `src/NLISSN.Rule.Generator` is attached to `NLISSN.Rules` as an analyzer-only project reference.
- `GeneratedRuleCatalog` emits four strongly typed lists and `84` direct factories. The stage baselines are default Core `19/12/5/32` and all features `23/16/9/36`.
- All five registered features have real `Mark`, `Propagate`, `Lift`, and `Propose` entries. The two method-deletion features use real linkage revalidation in Propagate and structured `MethodDeletionLiftPayload` output in Lift.
- `RuleRegistry` was removed. `RulePipelineComposer.Compose(RuleSelection)` is the runtime composition root; `RuleSelectionAdapter` preserves legacy configuration mapping.
- Generator-driver coverage includes valid generation, metadata/constructibility/identity errors, fixed-type conflicts, stable emission, and `NLRCG011` atomic failure.
- The latest core tier run was `Run-TestTiers.ps1 -Fast -Host`: Unit `80/80`, Contract `307/307`, Host `616/616`, exit code `0`. The Contract build emitted one non-failing Verify solution-discovery warning.

The full Performance tier was not part of this lightweight core-test run. Its external Terraria fixture remains an environment-dependent boundary and must be reported separately from the core acceptance result.

## Diagnostic and rollback policy

Stop the migration and retain the manual registry if:

- a concrete rule is missing from the generated catalog;
- a non-rule helper is included;
- a feature-off rule appears in the default pipeline;
- a disabled rule disappears from graph declarations;
- generated order differs from the manual baseline;
- RuleId, graph nodes, evidence, decisions, rewrites, or diffs differ;
- generator errors become warnings or produce partial catalog output;
- runtime reflection is required to construct a rule;
- a rule must be renamed only to satisfy generation.

Rollback is the last passing checkpoint, not git reset --hard. Preserve generated source, diagnostic output, and comparison snapshots for diagnosis.

## Acceptance checklist

- [x] Every concrete four-stage rule has explicit feature metadata.
- [x] Missing metadata fails at compile time with a stable diagnostic.
- [x] Generator-driver tests cover valid generation and all required invalid cases.
- [x] Generated code calls concrete constructors directly and contains no runtime scan/reflection construction.
- [x] `RulePipelineComposer` is the only runtime composition root; the manual `RuleRegistry` is removed.
- [x] Default and all-feature stage snapshots match the frozen baseline exactly.
- [x] Disabled rule collections and graph declarations match existing behavior.
- [x] RuleId validation remains active.
- [x] DOP 1, 2, and 16 behavior equivalence is covered by the existing Host/Contract equivalence tests.
- [x] Focused Host/Contract tests, fast/host tiers, init, and generated-source checks have recorded results.
- [x] Final harness consistency and changed-document link checks passed after this documentation update.
- [x] Design, execution plan, Context/feature_list.json, and Context/progress.md record the implemented scope and remaining boundaries.

## Suggested checkpoint commits

1. test: freeze rule catalog completeness baseline
2. feat: add rule registration metadata contract
3. feat: add rule catalog incremental generator diagnostics
4. chore: annotate concrete rules with feature metadata
5. refactor: compose rules from generated catalog
6. test: prove generated rule catalog behavior equivalence
7. docs: record generated rule catalog execution and boundaries

Do not commit unrelated existing changes in the shared worktree. If a checkpoint overlaps existing user work, keep it uncommitted and report the overlap instead of destructive cleanup.
