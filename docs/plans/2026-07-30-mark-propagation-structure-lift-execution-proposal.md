# Mark Propagation And Structural Lift Execution Proposal

## Goal

Keep the current `MarkRecord(SyntaxNode)` model. A mark represents the smallest
currently available syntax fragment, referred to in this document as a token-level
mark. It does not require changing `MarkRecord` to Roslyn `SyntaxToken`.

Make the stage boundary explicit:

```text
Mark -> Propagate token-level marks -> Lift expression or structure conclusions -> Propose
```

`Propagate` may derive a new mark through a semantic or data-flow relationship. It
must not decide that a syntax fragment is a structure, decide that it is fully
covered, or emit a structure-decision payload. `Lift` owns coverage checks and
structure conclusions.

## Accepted Decisions

1. Add `StructuralKind` in `NLISSN.Core.Lifting` with exactly these values:

   ```csharp
   public enum StructuralKind
   {
     Assignment,
     LocalDefinition,
     If,
     Loop,
     Switch,
     ConditionalExpression,
     Return
   }
   ```

2. `LogicalExpression` is not a `StructuralKind`. Logical `&&` and `||` reduction
   remains a non-structural expression lift with a distinct semantic tag and a
   lift-owned payload.
3. A `MarkRecord.SyntaxNode` does not become structural merely because its Roslyn
   kind is `VariableDeclaratorSyntax`, `IfStatementSyntax`, or another container.
   It becomes structural only when a lift result declares `StructuralKind`.
4. `PropagatedMarkRecord.Payload` is reserved for non-structural propagation
   evidence. It must not contain a logical-reduction payload, an if-completion
   payload, or a payload whose purpose is a structural decision.
5. `LiftedMarkRecord` carries an optional lift payload and an optional
   `StructuralKind`. A logical-expression lift has a payload but no
   `StructuralKind`; an if lift has `StructuralKind.If` and may carry the data
   needed to build its decision.
6. Coverage uses existing `SyntaxNode` spans and marks. A structure evaluator first
   accepts an exact mark for the relevant node; otherwise it accepts coverage only
   when every required child fragment is covered by one or more marks. It must not
   treat a descendant mark as coverage of an unmarked sibling or parent.

## Current Findings

The current worktree violates the target boundary in several places:

- `AtomicPropagationRuleBase.SharedAllowedPropagateNodeKinds` includes logical,
  `if`, `else`, `switch`, `switch section`, block, statement, and return kinds.
- `LogicalConditionPropagationRule` and `LogicalOperandGroupPropagationRule` turn
  atomic marks into a logical host and `LogicalHostPayload` during Propagate.
- `IfStructureCompletionPropagationRule` and
  `DeclarationIfStructureCompletionPropagationRule` emit an `IfStatement` or
  `ElseClause` with `IfStructureCompletionPayload` during Propagate.
- `PropagationHelpers.EnumerateIfStructureCompletionPropagations` calls
  `ProposalHelpers.TryBuildIfStructureCompletionPayload` from Propagate. Its input
  is one marked node, so it cannot prove that all required condition fragments are
  marked.
- `LogicalExpressionProposalRule`, `IfStructureProposalRule`, and
  `DeclarationIfStructureProposalRule` consume propagation payloads directly.
- `MarkLiftingEngine.Run` and `RuleGraphAnalysisExecutor.CreateLifterNode` discard
  propagated marks that have payloads. This is an explicit bypass around the Lift
  phase for the facts that most need a lift decision.
- `AtomicIfStructureLiftingRule` and `DeclarationIfStructureLiftingRule` already
  produce `IfStructure` marks, but their proposal rules do not consume those lift
  results as the decision source.

The existing `SwitchStructureLiftingHelpers` is the desired direction: it inspects
provisional marks in Lift, tests coverage, and only then creates a structure mark.
Its coverage predicate must be generalized rather than copied blindly.

## Non-Goals

- Do not change `MarkRecord` from `SyntaxNode` to `SyntaxToken`.
- Do not add a character-level lexer, a parallel mark representation, compatibility
  aliases, family routing, or `GroupKey` scheduling.
- Do not alter CPG construction, DOP defaults, rewrite conflict policy, or unrelated
  declaration/parameter propagation behavior.
- Do not classify every Roslyn syntax kind. `StructuralKind` is only the rule
  structures listed above.

## Execution Plan

### Phase 1: Define Stage Contracts And Tests

**Files:**

- Add: `src/NLISSN.Core/Lifting/StructuralKind.cs`
- Modify: `src/NLISSN.Core/Lifting/LiftedMarkRecord.cs`
- Modify: `src/NLISSN.Core/Propagation/PropagatedMarkRecord.cs`
- Modify: `src/NLISSN.Core/Marking/MarkRecord.cs`
- Modify: `tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs`
- Modify: `tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`

**Steps:**

1. Add `StructuralKind` with the exact seven values above. Do not add
   `LogicalExpression`.
2. Extend `LiftedMarkRecord` with optional `StructuralKind? StructureKind` and
   optional `object? Payload`. Keep both optional so ordinary expression-host lifts
   retain their current construction shape.
3. Document `RuleOutputKind` as legacy provenance only. Do not use it to identify a
   structure; `LiftedMarkRecord.StructureKind` is the sole structure conclusion.
4. Add failing tests proving that Propagate cannot emit a lift payload or a result
   declared as a structure. A rule that returns an `IfStatementSyntax` as a generic
   propagated mark is not itself an error, but no `StructuralKind` or structural
   payload may be attached before Lift.
5. Add tests that assert the enum has only the seven approved values and no logical
   member.

**Acceptance gate:** focused tests fail before the engine validation and record
contracts are updated, then pass once the phase is implemented.

### Phase 2: Move Logical Reduction Into Lift

**Files:**

- Move or replace: `src/NLISSN.Core/Propagation/AtomicStructuredPropagationPayloads.cs`
- Modify: `src/NLISSN.Rules/Propagate/AtomicPropagationRules.cs`
- Modify: `src/NLISSN.Rules/Propagate/Support/PropagationFacts.cs`
- Add: `src/NLISSN.Rules/Lift/AtomicLogicalExpressionLiftingRule.cs`
- Add: `src/NLISSN.Rules/Lift/Support/LogicalExpressionLifter.cs`
- Modify: `src/NLISSN.Application/RuleRegistry.cs`
- Modify: `src/NLISSN.Rules/Propose/LogicalExpressionProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/ProposalFacts.cs`
- Modify: `tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`

**Steps:**

1. Delete `LogicalConditionPropagationRule` and `LogicalOperandGroupPropagationRule`.
   Remove their registry entries, contracts, and tests that expect a propagated
   logical host or `LogicalHostPayload`.
2. Move `LogicalHostPayload` to Lifting and rename it
   `LogicalExpressionReductionPayload`. It belongs to an expression lift, not to
   propagation and not to `StructuralKind`.
3. Add `AtomicLogicalExpressionLiftingRule`. It consumes atomic and propagated
   token-level marks and emits only `LogicalAndExpression` or
   `LogicalOrExpression` with tag `Atomic.LogicalReduction`.
4. Move `TryBuildLogicalHostPayload` to a lift helper. It must aggregate all input
   marks for one logical host before deciding removable and surviving operands. It
   may produce a payload only when both sets are non-empty and disjoint.
5. Change `LogicalExpressionProposalRule` to consume
   `Atomic.LogicalReduction` lift results and enumerate lift payloads. It must not
   inspect `propagatedMarks` for logical payloads.
6. Add cases for a single removable operand, multiple removable operands, nested
   `&&`/`||`, no survivor, and no structural classification.

**Acceptance gate:** logical rewrite decisions remain identical for existing
short-circuit fixtures, while `PropagatedMarks` contains neither logical host marks
nor logical-reduction payloads.

### Phase 3: Move If Completion And Coverage Into Lift

**Files:**

- Delete: `src/NLISSN.Rules/Propagate/DeclarationIfStructureCompletionPropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/AtomicPropagationRules.cs`
- Modify: `src/NLISSN.Rules/Propagate/Support/PropagationFacts.cs`
- Modify: `src/NLISSN.Rules/Lift/AtomicIfStructureLiftingRule.cs`
- Modify: `src/NLISSN.Rules/Lift/DeclarationLiftingRules.cs`
- Modify: `src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs`
- Add: `src/NLISSN.Rules/Lift/Support/MarkCoverage.cs`
- Modify: `src/NLISSN.Application/RuleRegistry.cs`
- Modify: `src/NLISSN.Rules/Propose/IfStructureProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/DeclarationProposalRules.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/ProposalFacts.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Modify: `tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs`

**Steps:**

1. Delete both `IfStructureCompletionPropagationRule` implementations and remove
   their registrations. Delete `EnumerateIfStructureCompletionPropagations` from
   propagation support.
2. Move `IfStructureCompletionPayload` and its enum into Lifting. Rename them to
   `IfStructureLiftPayload` and `IfStructureLiftKind` to prevent stage confusion.
3. Implement `MarkCoverage.IsCovered(requiredNode, marks)`. It must use exact node
   identity or a complete partition of required child fragments. Nested marks count
   only when they cover their required fragment; any unmarked sibling makes the
   parent incomplete.
4. Change `IfStructureLiftingHelpers` to evaluate the candidate if condition from
   the complete mark set. Emit `StructureKind = StructuralKind.If` only if the
   condition is covered. Retain the existing conservative handling of incomplete
   syntax and nullable `Else` members.
5. Build the if payload only after coverage succeeds, attach it to the lifted record,
   and make the emitted lift mark point to the actual decision node.
6. Change atomic and declaration if proposal rules to consume their respective
   `*.IfStructure` lift contracts and lift payloads. Remove all direct propagation
   payload reads.
7. Add negative cases: one condition operand marked; a marked descendant plus an
   unmarked sibling; malformed `if`; an `else if` with an incomplete tail; and a
   condition made fully removable only after logical reduction.

**Acceptance gate:** only Lift can create `StructuralKind.If`; an if deletion or
replacement is impossible when its required condition fragments are incomplete.

### Phase 4: Classify The Remaining Structural Lifts

**Files:**

- Modify: `src/NLISSN.Rules/Lift/Structures/SwitchStructureLifter.cs`
- Modify: `src/NLISSN.Rules/Lift/AtomicSwitchStructureLiftingRule.cs`
- Modify: `src/NLISSN.Rules/Lift/DeclarationLiftingRules.cs`
- Modify: `src/NLISSN.Rules/Lift/Support/ExpressionHostResolver.cs`
- Modify: `src/NLISSN.Rules/Propose/ControlStructureRemovalProposalRule.cs`
- Modify: `src/NLISSN.Rules/Propose/DefaultRemovalProposalRule.cs`
- Modify: focused Unit and Host tests identified by the changed lifters

**Steps:**

1. Set `StructuralKind.Switch` only on switch-section and switch-statement lift
   results that pass the existing executable-statement coverage check.
2. Split expression-host output from structural output. Assign
   `Assignment`, `LocalDefinition`, `ConditionalExpression`, `Return`, and `Loop`
   only in dedicated lift branches that have a documented coverage predicate.
3. Do not label a generic expression host as a structure merely because it is an
   ancestor of one of these forms.
4. Make structure-removal proposals consume lifted structural results. Filter on
   `StructureKind`, not `RuleOutputKind` and not a broad list of Roslyn kinds.
5. Add one positive and one incomplete-coverage negative test for every
   `StructuralKind` before enabling its proposal path. Leave a kind unproduced if a
   safe coverage definition is not yet implemented.

**Acceptance gate:** every structural deletion decision has an upstream lifted mark
with one approved `StructuralKind`; no propagation result is sufficient by itself.

### Phase 5: Enforce The Boundary And Remove Dead Paths

**Files:**

- Modify: `src/NLISSN.Core/Propagation/PropagationEngine.cs`
- Modify: `src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`
- Modify: `src/NLISSN.Core/Validation/RuleBindingValidator.cs`
- Modify: `src/NLISSN.Core/Marking/MarkRecord.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/ProposalFacts.cs`
- Modify: affected contract, Host, and Unit tests

**Steps:**

1. Add propagation-output validation that rejects a `PropagatedMarkRecord` with a
   lift payload or any `StructuralKind` conclusion.
2. Remove the two existing payload filters that silently drop payload-bearing
   propagation results before Lift. All propagated marks must instead reach Lift
   through their declared syntax-tag edges.
3. Add lift-output validation: `StructureKind` is allowed only on
   `LiftedMarkRecord`, must be one of the enum values, and must agree with the
   lift rule's declared `SyntaxKind` contract.
4. Update `RuleBindingValidator`'s registered payload list to accept only the new
   lift payload types. Remove `LogicalHostPayload` and
   `IfStructureCompletionPayload` from propagation validation.
5. Delete obsolete output kinds and semantic tags only after all callers use lift
   contracts. Do not leave aliases such as `Atomic.LogicalHost` or
   `Atomic.IfCompletion`.

**Acceptance gate:**

```powershell
rg -n "LogicalHostPayload|IfStructureCompletionPayload|LogicalConditionPropagationRule|LogicalOperandGroupPropagationRule|IfStructureCompletionPropagationRule|DeclarationIfStructureCompletionPropagationRule|Atomic\.LogicalHost|Atomic\.IfCompletion|Declaration\.IfCompletion" src tests
```

Expected: no source or test hits, excluding this proposal and intentional migration
test names removed in the same phase.

### Phase 6: Verification And Documentation

Run sequentially because the shared worktree has concurrent rule-DAG changes:

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path

dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Application\NLISSN.Application.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false

dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore --filter "FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~DefaultRemovalProposalRuleTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore --filter "FullyQualifiedName~PropagationRuleExpansionTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~GraphAnalyzerTests"

pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

Then run the existing DOP 1 and DOP 16 default-pipeline equivalence coverage. Compare
ordered marks, lifted structure kinds, decisions, rewritten source, diff, and graph
node statuses. Record only executed commands and results in `feature_list.json` and
`progress.md` if this proposal is accepted for implementation.

## Definition Of Done

- `StructuralKind` contains only the seven approved structures.
- Logical reduction is represented as a non-structural Lift result.
- Propagation produces relationship-derived marks only and no structural payload or
  structural conclusion.
- Each enabled structural decision has an upstream lift with a complete-coverage
  proof and an approved `StructuralKind`.
- Atomic and declaration if decisions consume lift outputs, not propagation payloads.
- Focused Unit/Host tests, sequential builds, DOP equivalence, harness consistency,
  and `git diff --check` pass.
