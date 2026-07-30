# Syntax-Tag Rule Contract DAG Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** Make syntax-node kinds and semantic tags the only rule-DAG data contracts, removing rule-family, terminal-fact, and structure-role routing.

**Architecture:** Each rule directly declares consumed and produced syntax contracts. A contract contains allowed `SyntaxKind` values and one `RuleSemanticTag`; `RuleInputCardinality` belongs to the consuming rule definition, not to individual input contracts. The compiler creates every edge from matching contracts, and execution supplies only values carried by those edges.

**Tech Stack:** .NET 10, Microsoft.CodeAnalysis.CSharp, xUnit, `NLISSN.Core.Pipeline`, and `NL.Concurrency`.

---

## Accepted Decisions

- `RuleFactDomain`, `RuleTerminalFactSelector`, and `RuleTerminalConsumesContract` are deleted. No equivalent family, stage, or terminal selector replaces them.
- `SObjectRuleSet` and `ClassRuleSet` are deleted. Default registration becomes flat stage lists; no replacement rule-family registration object is introduced.
- `Consumes` contains direct `RuleConsumedSyntax` values. Each value contains `SyntaxKinds` and `SemanticTag` only.
- `Produces` contains direct `RuleProducedSyntax` values with the same two fields.
- `RuleInputCardinality` moves to `IRuleDefinition` and the four stage bases. It applies to every declared input of that rule. The default is `All`.
- A contract declares syntax shape, not a runtime `SyntaxNode` instance. A rule emits actual `SyntaxNode` instances after its Roslyn and semantic checks succeed.
- An input matches an output only when the semantic tag is equal and every producer `SyntaxKind` is accepted by the consumer. Partial overlap is a compile-time error.
- `RuleSyntaxStructureKind`, `RuleSyntaxStructureRole`, `RuleSyntaxStructureCatalog`, and their factory helpers leave the DAG contract path. Rule-local Roslyn logic remains responsible for direct-member checks such as `if` condition, `else`, and `else if`.

## Target API

```csharp
public sealed record RuleConsumedSyntax(
  IReadOnlyList<SyntaxKind> SyntaxKinds,
  RuleSemanticTag SemanticTag);

public sealed record RuleProducedSyntax(
  IReadOnlyList<SyntaxKind> SyntaxKinds,
  RuleSemanticTag SemanticTag);

public sealed record RuleConsumesContract(
  IReadOnlyList<RuleConsumedSyntax> Inputs);

public sealed record RuleProducesContract(
  IReadOnlyList<RuleProducedSyntax> Outputs);

public interface IRuleDefinition
{
  RuleInputCardinality InputCardinality { get; }
  RuleConsumesContract Consumes { get; }
  RuleProducesContract Produces { get; }
}
```

`RuleInputCardinality.All` requires all matching producers to be complete before execution.
`ExactlyOne` rejects zero or multiple matching producers during graph compilation. `Optional`
permits zero matching producers. It is rule-level policy and never appears in an input port.

## Non-Goals

- Do not infer edges from `RuleId`, type names, `SObject`, `Class`, `GroupKey`, AST ancestry, or option names.
- Do not create a family collector, aggregate node, terminal node, or fixed-point region.
- Do not alter rule semantics, rewrite conflict policy, runtime DOP defaults, or CPG admission.
- Do not preserve compatibility aliases for removed contract types.

### Task 1: Lock the New Contract Behavior

**Files:**
- Modify: `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/RuleGraphCompilerTests.cs`

**Step 1: Write failing syntax-tag contract tests.**

Cover a producer and consumer with equal tag and compatible `SyntaxKind`; different tags;
producer kinds outside the consumer's accepted list; `All`, `ExactlyOne`, and `Optional`
as rule-level policies; and a consumer with no declared producer.

**Step 2: Run the focused tests.**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore --filter "FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~RuleGraphCompilerTests"
```

Expected: the new tests fail until the direct syntax-tag model exists.

**Step 3: Replace terminal-domain assertions.**

Delete the assertions that expect 25 SObject and 15 Class terminal dependencies. Add
assertions that every compiled edge has a `RuleConsumedSyntax` contract and that the graph
contains no fact-domain or terminal input property.

**Step 4: Commit.**

```text
test: define syntax-tag rule DAG contracts
```

### Task 2: Replace the Contract Model and Compiler

**Files:**
- Modify: `src/NLISSN.Rule/RuleStructureContract.cs`
- Modify: `src/NLISSN.Rule/IRuleDefinition.cs`
- Modify: `src/NLISSN.Rule/RuleGraph.cs`
- Modify: `src/NLISSN.Rule/RuleStructureContractGraphCompiler.cs`
- Modify: `src/NLISSN.Rule/RuleGraphCompiler.cs`
- Modify: `src/NLISSN.Rule/RuleGraphExecutor.cs`

**Step 1: Replace selector records.**

Delete `MarkedStructureSelector`, `RuleConsumedStructure`, terminal-fact records, and
their factory helpers. Add `RuleConsumedSyntax`, `RuleProducedSyntax`, and compatibility
validation for non-empty, distinct `SyntaxKind` lists and non-empty semantic tags.

**Step 2: Move cardinality to rule definitions.**

Add `InputCardinality` to `IRuleDefinition` and defaults in the four stage base classes.
Remove cardinality from each input contract and apply the consumer rule's policy when the
contract compiler validates its complete producer set.

**Step 3: Materialize syntax-tag edges.**

Replace every structure-selector graph edge and port index with a direct input/output
syntax contract. Keep one ready prerequisite per producer even when it matches several
consumer inputs. Fail with the consumer node ID, tag, and syntax kinds for incompatible or
ambiguous contracts.

**Step 4: Route node results by syntax-tag contracts.**

Validate each emitted mark's `SyntaxNode.Kind()` and `SemanticTag` against its producer
output. A consumer obtains only values from the exact dependencies selected by the graph.

**Step 5: Run Task 1 tests.**

Expected: focused contract and graph tests pass without any terminal-fact types.

**Step 6: Commit.**

```text
refactor: compile rule DAG from syntax-tag contracts
```

### Task 3: Remove Family and Terminal Routing

**Files:**
- Delete: `src/NLISSN.Core/Propagation/ClassPropagationRuleBase.cs`
- Modify: `src/NLISSN.Core/Propagation/TargetPropagationRuleBase.cs`
- Modify: `src/NLISSN.Core/Marking/RuleDefinitionMark.cs`
- Modify: `src/NLISSN.Core/Propagation/RuleDefinitionPropagate.cs`
- Modify: `src/NLISSN.Core/Lifting/RuleDefinitionLift.cs`
- Modify: `src/NLISSN.Core/Decision/RuleDefinitionPropose.cs`
- Modify: `src/NLISSN.Application/Analysis/RulePipeline.cs`
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`
- Modify: `src/NLISSN.Rules/Mark/TargetAtomicMarkRules.cs`
- Modify: `src/NLISSN.Rules/Mark/AtomicExpressions/TargetAtomicExpressionMarkRuleBase.cs`
- Modify: `src/NLISSN.Rules/Mark/TypeMarkRules.cs`
- Modify: `src/NLISSN.Rules/Lift/TargetExpressionHostLiftingRule.cs`
- Modify: `src/NLISSN.Rules/Lift/TargetIfStructureLiftingRule.cs`
- Modify: `src/NLISSN.Rules/Lift/TargetSwitchStructureLiftingRule.cs`
- Modify: `src/NLISSN.Rules/Lift/TypeLiftingRules.cs`
- Modify: `src/NLISSN.Rules/Propose/DefaultRemovalProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/ControlStructureRemovalProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/TypeProposalRules.cs`

**Step 1: Delete domain and terminal members.**

Remove every `FactDomain` and `TerminalConsumes` declaration, the pipeline terminal-edge
expansion, terminal graph dependency field, and compiler validation branch.

**Step 2: Declare direct syntax-tag inputs for every current terminal consumer.**

Port each basic propagation, host lift, default proposal, and control proposal to the
specific syntax-tag outputs it actually reads. Split a rule before assigning an unrelated
syntax tag to one broad input. Do not recreate broad default-family fan-in.

**Step 3: Keep direct Roslyn ownership local.**

Where a rule needs `if` member relationships, validate them in the rule helper producing
the mark and attach distinct tags to the produced concrete node kinds. The DAG only matches
the emitted node kind and tag.

**Step 4: Run the focused pipeline tests.**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~LogicalConditionMarkAnalyzerTests|FullyQualifiedName~DecisionStructureValidationTests"
```

Expected: direct graph analysis preserves seed, propagated, lifted, and decision results.

**Step 5: Commit.**

```text
refactor: remove terminal fact routing from deletion rules
```

### Task 4: Remove SObject and Class Rule-Set Registration

**Files:**
- Modify: `src/NLISSN.Application/Catalog/RuleSet.cs`
- Modify: `src/NLISSN.Application/Catalog/RuleCatalog.cs`
- Modify: `src/NLISSN.Application/RuleRegistry.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Delete `SObjectRuleSet` and `ClassRuleSet`.**

Move their concrete rule registrations into flat default stage lists. Remove their IDs,
all composition assertions, and any catalog validation that makes their membership a
scheduler or dependency concept.

**Step 2: Preserve capability uniqueness.**

Keep validation of non-blank and duplicate `CapabilityId`, but associate diagnostics with
the concrete rule type rather than a rule-set ID.

**Step 3: Update test fixtures.**

Replace `IRuleSet` fixtures only where they exercise the removed registration API. Test the
flat default pipeline's stable rule order and the absence of SObject/Class registration
types.

**Step 4: Commit.**

```text
refactor: flatten default rule registration
```

### Task 5: Delete the Structure-Role DAG Layer

**Files:**
- Delete: `src/NLISSN.Rule/RuleSyntaxStructure.cs`
- Delete: `src/NLISSN.Rule/RuleSyntaxStructureCatalog.cs`
- Modify: `src/NLISSN.Core/Decision/DecisionModel.cs`
- Modify: `src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`
- Modify: `src/NLISSN.Core/Propagation/PropagationEngine.cs`
- Modify: all affected rule and test files identified by `rg -n "RuleSyntaxStructure|MarkedStructureSelector|RuleConsumedStructure" src tests`

**Step 1: Move remaining direct-member predicates.**

Preserve `if` / `else if` safety predicates in the rule helpers that construct marks. Do
not retain a shared catalog merely to classify DAG ports.

**Step 2: Replace observed-structure compatibility adapters.**

Compatibility stage engines derive observed syntax-tag outputs from marks and their
producer declarations. They must not reconstruct rule-family or structure-role routing.

**Step 3: Delete obsolete catalog tests and add direct syntax-tag output tests.**

Keep coverage for incomplete syntax and invalid direct-member relationships at the rule
helper level where they are now enforced.

**Step 4: Commit.**

```text
refactor: remove structure-role DAG contracts
```

### Task 6: Verify Equivalence and Documentation

**Files:**
- Modify: `设计docs/目前设计/规则DAG.md`
- Modify: `设计docs/目前设计/deletion-pipeline.md`
- Modify: `progress.md`
- Modify: `feature_list.json`

**Step 1: Run sequential builds.**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Application\NLISSN.Application.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
```

**Step 2: Run graph and behavior tests sequentially.**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build --filter "FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~RuleGraphCompilerTests|FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~MarkRuleRegistryCoverageTests"
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build --filter FullyQualifiedName~LayoutArchitectureTests
```

**Step 3: Prove DOP equivalence.**

Run the existing default-pipeline graph regression at DOP 1 and DOP 16. Compare ordered
marks, decisions, rewritten source, diff, node statuses, and telemetry.

**Step 4: Update current design documents.**

State that automatic edges use syntax kinds and semantic tags only. Remove terminal facts,
structure roles, and SObject/Class rule-set claims. Do not update historical plans.

**Step 5: Run final repository checks.**

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
rg -n "RuleFactDomain|RuleTerminal|MarkedStructureSelector|RuleConsumedStructure|RuleSyntaxStructureKind|RuleSyntaxStructureRole|SObjectRuleSet|ClassRuleSet" src tests
```

Expected: the search has no source or test hits; generated build artifacts are excluded.

**Step 6: Commit.**

```text
docs: record syntax-tag rule DAG contracts
```

## Acceptance Criteria

- The compiled DAG has no rule-family, terminal-fact, structure-role, or rule-ID routing.
- Every edge has a matching producer output and consumer input based only on syntax kinds
  and a semantic tag.
- `RuleInputCardinality` is defined once per rule definition and is absent from syntax
  contract records.
- No `SObjectRuleSet` or `ClassRuleSet` type or registration path remains.
- Default-pipeline behavior and DOP 1/16 snapshots remain equivalent.
