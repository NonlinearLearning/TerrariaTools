# Remove Closed Atomic And Declaration Chains Implementation Plan

**Goal:** Replace the closed `Atomic.*` and `Declaration.*` rule-routing chains with one fact-composed rule DAG while preserving atomic mark proof, declaration-signature safety, rewrite behavior, and DOP equivalence.

**Architecture:** Keep `Mark -> Propagate -> Lift -> Propose`. `RuleSemanticTag` becomes a neutral fact port such as `Target.Expression`, `Target.TypeSyntax`, `Flow.LocalDefinition`, or `Lift.IfStructure`; a `RuleEvidenceOrigin` carried by the record preserves whether the proof originated from an atomic expression, a declaration/type position, or both. `Lift` is the composition seam: shared lifters consume compatible facts from either origin and emit a single normalized host or structural fact. Declaration-only parameter, delegate, and callsite payloads remain relation facts and retain their dedicated proposal safety policies.

**Tech Stack:** C# / .NET 10, Roslyn syntax and semantic APIs, NLCPG, xUnit, existing `RuleGraph` and `RuleStructureContractGraphCompiler`.

---

## Scope And Invariants

- Remove `Atomic.*` and `Declaration.*` as routing partitions. Do not delete atomic-expression matching, type-syntax matching, declaration-host resolution, parameter usage analysis, or rewrite policies.
- Keep the four stages. Do not add a fifth execution stage or restore `GroupKey`, rule-family scheduling, or compatibility aliases.
- Preserve a proof source for every emitted mark. A composed fact accumulates origins; it does not replace one input fact with an arbitrary winner.
- Keep `PropagatedMarkRecord.Payload` limited to relation evidence. `LiftedMarkRecord` remains the only carrier of structural conclusions and logical/if lift payloads.
- Keep `RuleProducedSyntax` contracts exact. A broad producer output must not create accidental graph edges merely because two sources share a neutral port.
- Work sequentially: this is a shared worktree with concurrent rule-DAG edits.

## Fact Vocabulary

Use these ports for the migration. Split a port only when its payload schema or safety policy differs.

| Current families | Neutral port | Evidence origin | Owner after migration |
| --- | --- | --- | --- |
| `Atomic.Atomic`, `Declaration.ExpressionTarget` | `Target.Expression` | `AtomicExpression`, `DeclarationExpression` | Mark |
| `Declaration.TypeSyntaxTarget` | `Target.TypeSyntax` | `DeclarationType` | Mark |
| `Declaration.DeclarationTarget` | `Target.Declaration` | `DeclarationName` | Mark |
| atomic/declaration local-definition tags | `Flow.LocalDefinition` | source origin union | Propagate |
| atomic/declaration symbol-reference tags | `Flow.SymbolReference` | source origin union | Propagate |
| `Declaration.DeclarationHost` | `Relation.DeclarationHost` | `DeclarationType` | Propagate |
| declaration parameter/delegate/extension tags | `Relation.ParameterUsage`, `Relation.DelegateUsage`, `Relation.ExtensionUsage` | `DeclarationType` | Propagate |
| both expression-host tags | `Lift.ExpressionHost` | source origin union | Lift |
| atomic logical output | `Lift.LogicalReduction` | source origin union | Lift |
| both if/switch/control tags | `Lift.IfStructure`, `Lift.SwitchStructure`, `Lift.ControlStructure` | source origin union | Lift |

`RuleEvidenceOrigin` is provenance metadata. It must not be used by `RuleStructureContractGraphCompiler` to create edges. A consumer that needs declaration-only safety checks inspects the provenance and typed relation payload explicitly.

## Task 1: Stabilize The Current Migration Baseline

**Files:**
- Modify: `tests/NLISSN.HostTests/Mark/LogicalConditionMarkAnalyzerTests.cs`
- Modify: `tests/NLISSN.HostTests/Mark/MarkRuleEffectTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`

**Step 1: Replace expectations for removed propagation-owned logical hosts.**

- Make `LogicalConditionMarkAnalyzerTests` include `AtomicLogicalExpressionLiftingRule` in its effective-mark helper.
- Replace assertions over propagated logical marks with assertions over `LiftedMarkRecord` whose tag is `Lift.LogicalReduction` and whose payload is `LogicalExpressionReductionPayload`.
- Keep the fixtures for nested precedence, parenthesized expressions, multiple target hits, no survivor, and non-logical conditions.

**Step 2: Add the missing graph-execution cases.**

- Add a case where an assignment or initializer creates a propagated target fact that participates in logical reduction.
- Add a case where a condition becomes fully removable only after logical reduction and must reach the if lifter through declared graph edges.
- Assert ordered Lift outputs and final `RuleDecision` values, not private helper output.

**Step 3: Repair control-structure expectations around Lift ownership.**

- Convert old assertions that expect a propagated `for`, `while`, `do`, or `switch` mark into assertions for a `LiftedMarkRecord.StructureKind`.
- Add an incomplete-coverage case for every enabled structure kind.

**Step 4: Run the focused tests.**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~LogicalConditionMarkAnalyzerTests|FullyQualifiedName~MarkRuleEffectTests|FullyQualifiedName~DecisionStructureValidationTests"
```

Expected: all selected tests pass; none inspect a removed propagation logical-host fact.

**Step 5: Commit.**

```powershell
git add tests/NLISSN.HostTests/Mark tests/NLISSN.HostTests/Decision
git commit -m "test: stabilize lift-owned structural expectations"
```

## Task 2: Add Neutral Fact Ports And Provenance

**Files:**
- Modify: `src/NLISSN.Rule/RuleStructureContract.cs`
- Modify: `src/NLISSN.Core/Marking/MarkRecord.cs`
- Modify: `src/NLISSN.Core/Propagation/PropagatedMarkRecord.cs`
- Modify: `src/NLISSN.Core/Lifting/LiftedMarkRecord.cs`
- Modify: `src/NLISSN.Rule/RuleGraphExecutor.cs`
- Test: `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`

**Step 1: Write failing contract tests.**

- A mark has a neutral port and one evidence origin.
- A propagated or lifted mark preserves its source origin.
- A lift consuming two facts for the same anchor receives their union of origins.
- `RuleGraphExecutor.GetOutputs` routes by neutral port and `SyntaxKind`, never by origin.

**Step 2: Introduce the metadata types.**

Add a small immutable model in `RuleStructureContract.cs`:

```csharp
[Flags]
public enum RuleEvidenceOrigin
{
    None = 0,
    AtomicExpression = 1,
    DeclarationExpression = 2,
    DeclarationType = 4,
    DeclarationName = 8,
}
```

Keep `RuleSemanticTag` as the graph port in this task. Add canonical neutral tags in one owner; do not construct repeated string literals in concrete rules.

**Step 3: Extend records without introducing a second mark representation.**

- Add `RuleEvidenceOrigin Origins` to `MarkRecord`, defaulting to `None` only for legacy test construction.
- `PropagatedMarkRecord` and `LiftedMarkRecord` derive their origin set from their source mark unless a composition rule explicitly adds another input origin.
- Keep `Payload` ownership unchanged.

**Step 4: Make output validation origin-preserving.**

- `RuleGraphExecutor` continues to index values by `RuleProducedSyntax`.
- Add validation that a composed output has a non-empty origin set and that its origin is a subset/union of declared input evidence, as appropriate to the rule implementation.

**Step 5: Run the contract tests.**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~RuleGraphCompilerTests"
```

Expected: neutral ports route facts from more than one origin without creating origin-dependent edges.

**Step 6: Commit.**

```powershell
git add src/NLISSN.Rule src/NLISSN.Core/Marking src/NLISSN.Core/Propagation src/NLISSN.Core/Lifting tests/NLISSN.HostTests/Application
git commit -m "feat: add neutral rule fact ports and provenance"
```

## Task 3: Migrate Mark And Flow Facts To Neutral Ports

**Files:**
- Modify: `src/NLISSN.Rules/Mark/AtomicExpressions/AtomicExpressionMarkRuleBase.cs`
- Modify: `src/NLISSN.Rules/Mark/AtomicMarkRules.cs`
- Modify: `src/NLISSN.Rules/Mark/DeclarationMarkRules.cs`
- Modify: `src/NLISSN.Core/Propagation/AtomicPropagationRuleBase.cs`
- Modify: `src/NLISSN.Rules/Propagate/AtomicPropagationRules.cs`
- Modify: `src/NLISSN.Rules/Propagate/ObjectCreationDeclarationPropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/DeclarationSymbolReferencePropagationRule.cs`
- Test: `tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs`

**Step 1: Write failing source-to-flow tests.**

- Atomic identifier and declaration expression marks both emit `Target.Expression`, with different origins.
- TypeSyntax emits `Target.TypeSyntax` and never becomes a target expression by conversion alone.
- Atomic and declaration initializer paths both emit `Flow.LocalDefinition`.
- A composed origin set survives later `Flow.SymbolReference` propagation.

**Step 2: Migrate outputs one port at a time.**

- Replace `Atomic.Atomic` and `Declaration.ExpressionTarget` with `Target.Expression`.
- Replace local-definition and symbol-reference pairs with `Flow.LocalDefinition` and `Flow.SymbolReference`.
- Preserve `Target.TypeSyntax` and `Target.Declaration` as separate ports because their syntax and safety contracts differ.
- Update each consumer as the producing port changes; never leave an old tag alias.

**Step 3: Narrow producer syntax contracts.**

- Split `AtomicPropagationRuleBase.SharedAllowedPropagateNodeKinds` where a concrete propagator produces only a strict subset.
- Keep one `RuleProducedSyntax` per actual fact schema so the compiler can reject a partial syntax overlap.

**Step 4: Run propagation tests and source inventory.**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PropagationRuleExpansionTests|FullyQualifiedName~MarkRuleRegistryCoverageTests"
rg -n 'Atomic\.Atomic|Atomic\.Propagated|Atomic\.LocalDefinitionFromInitializer|Declaration\.ExpressionTarget|Declaration\.LocalDefinitionFromObjectCreation|Declaration\.SymbolReference' src tests
```

Expected: the search has no production hits; the tests prove equivalent seed and flow behavior.

**Step 5: Commit.**

```powershell
git add src/NLISSN.Rules/Mark src/NLISSN.Rules/Propagate src/NLISSN.Core/Propagation tests/NLISSN.HostTests/Propagation tests/NLISSN.HostTests/Mark
git commit -m "refactor: route target and flow facts through neutral ports"
```

## Task 4: Replace Parallel Expression And Structure Lifters

**Files:**
- Replace: `src/NLISSN.Rules/Lift/AtomicExpressionHostLiftingRule.cs`
- Replace: `src/NLISSN.Rules/Lift/AtomicIfStructureLiftingRule.cs`
- Replace: `src/NLISSN.Rules/Lift/AtomicSwitchStructureLiftingRule.cs`
- Replace: `src/NLISSN.Rules/Lift/DeclarationLiftingRules.cs`
- Modify: `src/NLISSN.Rules/Lift/AtomicLogicalExpressionLiftingRule.cs`
- Modify: `src/NLISSN.Rules/Lift/ControlStructureLiftingRules.cs`
- Modify: `src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs`
- Modify: `src/NLISSN.Rules/Lift/Structures/SwitchStructureLifter.cs`
- Test: `tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`
- Test: `tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs`

**Step 1: Write cross-origin Lift tests.**

- One condition receives a direct expression fact and a declaration-derived local-value fact; it produces one `Lift.LogicalReduction` or `Lift.IfStructure` output with both origins.
- A type-use fact inside a declaration reaches an expression host only when it has an explicit expression/value-flow relation.
- A declaration signature fact alone cannot authorize an expression deletion.
- A partially covered condition produces no structural Lift fact, regardless of source origin.

**Step 2: Introduce shared lifters.**

- Create one `ExpressionHostLiftingRule`, one `IfStructureLiftingRule`, one `SwitchStructureLiftingRule`, and one `ControlStructureLiftingRule`.
- Each consumes neutral target/flow/host ports and emits `Lift.*` ports.
- Preserve the existing `MarkCoverage` proof. Logical reduction remains non-structural.
- Merge origins for marks participating in the same lifted conclusion; retain all input records in evidence collection.

**Step 3: Keep declaration-only relations out of generic structural lifting.**

- `Relation.DeclarationHost`, parameter usage, delegate usage, and extension usage remain inputs to their dedicated proposal policies.
- Do not let a `DeclarationHostPayload` by itself satisfy `Target.Expression` or structural coverage.

**Step 4: Delete the parallel lifter classes only after all consumers use the neutral output ports.**

- Remove the Atomic/Declaration variants and their contract helpers.
- Update `RuleRegistry` in the same commit.

**Step 5: Run focused Lift and decision tests.**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~LogicalConditionMarkAnalyzerTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~GraphAnalyzerTests|FullyQualifiedName~PipelineComponentTests"
```

Expected: a single normalized structural output for each proven anchor; no behavior depends on the old chain family.

**Step 6: Commit.**

```powershell
git add src/NLISSN.Rules/Lift src/NLISSN.Application/RuleRegistry.cs tests/NLISSN.HostTests/Decision tests/NLISSN.HostTests/Application tests/NLISSN.HostTests/Mark
git commit -m "refactor: compose shared lift facts across rule origins"
```

## Task 5: Preserve Declaration Safety Policies Behind Relation Ports

**Files:**
- Modify: `src/NLISSN.Rules/Propagate/DeclarationHostPropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/MethodParameterUsagePropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/LocalFunctionParameterUsagePropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/IndexerParameterUsagePropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/DelegateUsageClassificationPropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/ExtensionMethodMappedCallsitePropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propose/DeclarationProposalRules.cs`
- Modify: `src/NLISSN.Rules/Propose/Declarations/DeclarationHostProposals.cs`
- Test: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Write failing policy tests.**

- Field/property/method-return, interface member, base type, and generic local declaration remain routed by `Relation.DeclarationHost`.
- Parameter shrink still requires its matching relation payload and all mapped callsites.
- An Atomic-origin fact cannot trigger signature shrink without declaration type and usage evidence.
- A composed origin can participate in a declaration proposal only when the relation payload supplies the required declaration safety data.

**Step 2: Rename contracts, not payload schemas.**

- Migrate `Declaration.*` tags to the `Relation.*` ports listed above.
- Keep `DeclarationHostPayload`, `MethodParameterUsagePayload`, and the other typed payloads until their consumers have a better domain name and identical validation coverage.
- Do not generalize their payloads into one `object` discriminator.

**Step 3: Update binding validation.**

- `RuleBindingValidator` accepts the existing relation payload types only from their matching neutral relation port.
- Reject a declaration-only payload on `Target.Expression`, `Lift.*`, or an unrelated relation port.

**Step 4: Run declaration regression tests.**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~BindingValidatorTests|FullyQualifiedName~DecisionStructureValidationTests"
```

Expected: declaration rewrite and parameter-shrink tests retain their current decision anchors and synchronized callsite edits.

**Step 5: Commit.**

```powershell
git add src/NLISSN.Rules/Propagate src/NLISSN.Rules/Propose src/NLISSN.Core/Validation tests/NLISSN.HostTests/Application tests/NLISSN.HostTests/Decision
git commit -m "refactor: preserve declaration safety through relation ports"
```

## Task 6: Normalize Proposal Contracts And Remove Closed-Chain Names

**Files:**
- Modify: `src/NLISSN.Rules/Propose/DefaultRemovalProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/LogicalExpressionProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/IfStructureProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/ControlStructureRemovalProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/ProposalFacts.cs`
- Modify: `src/NLISSN.Application/RuleRegistry.cs`
- Modify: `src/NLISSN.Application/Analysis/RulePipeline.cs`
- Test: `tests/NLISSN.HostTests/Application/RuleGraphCompilerTests.cs`
- Test: `tests/NLISSN.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`

**Step 1: Write failing graph tests.**

- A proposer consumes one normalized Lift port regardless of Atomic or Declaration provenance.
- A proposal requiring a declaration relation receives only its dedicated relation port.
- No enabled graph node has an `Atomic.*` or `Declaration.*` port.
- Disabled rules remain empty producers and do not satisfy a required input.

**Step 2: Migrate proposer contracts.**

- Logical, if, switch/control, and default-removal proposals consume `Lift.*` or neutral target/flow ports.
- Declaration proposals consume neutral relation ports plus typed payload validation.
- Keep decision conflict and merge policy unchanged.

**Step 3: Remove closed-chain registry entries and helpers.**

- Remove Atomic/Declaration-specific lifter registration and contract helper methods.
- Retain narrowly named Mark and relation rules when the syntax or safety proof is genuinely specialized.
- Delete old tags, aliases, and unused private semantic-tag fields.

**Step 4: Run structural graph tests and source inventory.**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleGraphCompilerTests|FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~MarkRuleRegistryCoverageTests"
rg -n '"Atomic\.|"Declaration\.|AtomicPropagationRuleBase|DeclarationLiftContracts' src tests
```

Expected: no routing-family symbols remain. Occurrences in historical documents are allowed; production and test code have none.

**Step 5: Commit.**

```powershell
git add src/NLISSN.Rules/Propose src/NLISSN.Application tests/NLISSN.HostTests/Application tests/NLISSN.HostTests/Mark
git commit -m "refactor: remove closed atomic and declaration rule chains"
```

## Task 7: End-To-End Equivalence, Documentation, And Cleanup

**Files:**
- Modify: `设计docs/目前设计/atomic-marking.md`
- Modify: `docs/developer-guide.md`
- Modify: `docs/quick-start.md` only if CLI behavior changes
- Modify: `progress.md` and `feature_list.json` only when an approved feature state changes
- Test: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Test: existing DOP 1/16 default-pipeline equivalence coverage

**Step 1: Add DOP and mixed-origin end-to-end fixtures.**

- Cover a single file and a directory where target evidence travels through object creation, local symbol use, a condition, and a declaration signature.
- Compare ordered marks including origins, propagated facts, Lift facts, decisions, rewritten source, diff, evidence graph, node statuses, and deterministic telemetry fields at DOP 1 and 16.
- Add a negative fixture proving that a type-signature fact cannot delete an unrelated expression, and an expression fact cannot shrink a signature without relation evidence.

**Step 2: Run sequential builds and focused/full Host tests.**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Application\NLISSN.Application.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
```

Expected: all builds and Host tests pass. Do not treat a failed or interrupted real-source run as performance or equivalence evidence.

**Step 3: Run repository checks.**

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

Expected: both commands exit `0`. Record any pre-existing harness issue separately from this migration.

**Step 4: Update current-design documentation.**

- Describe neutral fact ports, provenance, shared Lift, and declaration relation policies in `atomic-marking.md` and `developer-guide.md`.
- Remove references that describe Atomic and Declaration as closed rule-routing chains.
- Do not change CLI documentation unless options or output contracts changed.

**Step 5: Commit.**

```powershell
git add docs 设计docs tests
git commit -m "docs: describe fact-composed deletion rule DAG"
```

## Definition Of Done

- The runtime graph routes only by neutral fact ports and syntax contracts; origin provenance never partitions the graph.
- No enabled production or test rule declares an `Atomic.*` or `Declaration.*` routing port.
- Atomic mark granularity, target matching, CPG binding, and mark-region constraints remain intact.
- Declaration host and parameter/delegate/extension policies retain their typed relation payloads and safety checks.
- Shared Lift rules can combine evidence from both origins and preserve all provenance in evidence output.
- Logical, if, switch, and control conclusions are Lift-owned and remain coverage-proven.
- Focused and full Host tests, sequential builds, DOP 1/16 equivalence, harness consistency, and `git diff --check` pass.
