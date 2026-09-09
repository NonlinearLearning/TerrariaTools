
# Coverage Decision Constraints Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Prevent topology marks from being treated as complete structural coverage and resolve nested Delete/Replace candidates only through explicit evidence and edit footprints.

**Architecture:** Add typed coverage evidence and a requirement-aware evaluator; keep StructureComplete as a Lift-only proof. Add an explicit DecisionFootprint to proposal candidates and make RuleDecisionEngine the only semantic resolver. ApplicationService and PrototypeRewriter retain only orchestration and defensive overlap validation.

**Tech Stack:** C# / .NET 10, Roslyn syntax trees, NLISSN Mark/Propagate/Lift/Propose/Decision pipeline, xUnit, PowerShell.

---

## 1. Freeze the behavioral contract with regression tests

**Files:**
- Modify: tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs
- Modify: tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs
- Modify: tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs
- Create: tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs

### Step 1: Write the failing conflict tests

Add tests for the two existing semantic contracts:

- ready && s.IsReady with target s produces an outer Delete(if) result.
- ready || s.IsReady || fallback with target s produces a logical Replace, not an outer Delete(if).
- A synthetic outer Delete(if) plus inner logical Replace without a structural proof is rejected or returned as a conflict/skip; the inner candidate must not disappear silently.
- The same pair with an explicit structural proof resolves to only the outer Delete(if).
- Partial overlap between two replacements produces a conflict/skip instead of relying on proposal order.

Use the existing ApplicationService fixture for end-to-end cases and direct RuleDecisionEngine inputs for resolver-only cases.

### Step 2: Run the focused tests to establish the baseline

Run:

    dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionComplexTests|FullyQualifiedName~GraphAnalyzerTests.Analyze_LogicalAndCondition_RightTargetDeletesIf|FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~CoverageDecisionConflictTests" --logger "console;verbosity=minimal"

Expected: existing AND/OR tests pass; newly added proof/diagnostic tests fail because current coverage and decision filtering have no typed proof or footprint.

### Step 3: Commit only the regression tests

    git add tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs
    git commit -m "test: specify coverage and nested decision contracts"

Do not change production behavior in this task.

## 2. Add typed coverage evidence and preserve propagation payload

**Files:**
- Create: src/NLISSN.Core/Lifting/CoverageEvidence.cs
- Modify: src/NLISSN.Core/Marking/MarkRecord.cs
- Modify: src/NLISSN.Core/Marking/MarkRecordFactory.cs
- Modify: src/NLISSN.Core/Propagation/PropagatedMarkRecord.cs
- Modify: src/NLISSN.Rules/Propagate/LogicalExpressionPropagationRule.cs
- Test: tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs
- Test: tests/NLISSN.UnitTests/Decision/CoverageEvidenceTests.cs

### Step 1: Define the typed claims

Create immutable types in NLISSN.Core.Lifting:

- CoverageCapability: AtomicTarget, TopologyHost, ChildComposable, GlobalTarget.
- CoverageRequirement: LogicalReduction, ExpressionReplacement, StructureComplete.
- CoverageEvidence: original SyntaxNode, FactKind, capability, source stage, payload, and provenance nodes.

Keep DecisionActionKind out of these types. Add an optional capability to MarkRecord only as fact classification; StructureComplete must not be representable as a Mark capability.

### Step 2: Write capability contract tests

Assert that:

- FlowLogicalExpression is classified as TopologyHost.
- direct logical operands are classified as ChildComposable.
- a regular atomic target is AtomicTarget.
- global method target marks are GlobalTarget.
- Mark/Propagate records cannot declare a structural action or structural completion proof.
- the original PropagatedMarkRecord.Payload remains available when evidence is built.

### Step 3: Implement capability construction at mark creation sites

Extend MarkRecordFactory.Create with an optional capability parameter, then set capabilities at the producing rules. Do not infer capability from Reason strings. If a custom test rule omits a capability, classify it as unknown and reject it for StructureComplete requirements.

### Step 4: Run the capability tests

    dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~Coverage"
    dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleStructureContractTests"

Expected: PASS for the new typed-record tests; existing coverage behavior may still be unchanged until Task 3.

### Step 5: Commit the evidence model

    git add src/NLISSN.Core/Marking src/NLISSN.Core/Propagation src/NLISSN.Core/Lifting src/NLISSN.Rules/Propagate tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs tests/NLISSN.UnitTests
    git commit -m "feat: add typed coverage evidence"

## 3. Replace boolean coverage with a requirement-aware evaluator

**Files:**
- Modify: src/NLISSN.Rules/Lift/Support/MarkCoverage.cs
- Modify: src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs
- Modify: src/NLISSN.Rules/Lift/LogicalExpressionLiftingRule.cs
- Modify: src/NLISSN.Rules/Lift/Support/ExpressionHostResolver.cs if it constructs evidence
- Test: tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs
- Test: tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs

### Step 1: Add evaluator tests before changing callers

Test the evaluator directly:

- an exact TopologyHost match satisfies host lookup but fails StructureComplete;
- two ChildComposable operands can satisfy LogicalReduction;
- one of two operands returns Partial with the missing node key;
- an exact AtomicTarget can satisfy ExpressionReplacement but not structure deletion;
- unsupported or missing capability is rejected rather than treated as a generic mark;
- propagation payload and provenance appear in the returned evidence summary.

### Step 2: Implement CoverageResult and recursive evaluation

Replace MarkCoverage.IsCovered with an API accepting CoverageRequirement and the complete evidence list. The evaluator must:

- compare stable original-node keys;
- accept an exact node only when its capability is allowed by the requirement;
- recurse through children when the parent is not directly proven;
- return missing/rejected evidence instead of only false;
- never use a replacement node as source-tree coverage evidence.

Keep a short compatibility wrapper only for callers proven to require old logical-coverage semantics; do not allow the wrapper to satisfy StructureComplete.

### Step 3: Change IfStructureLifter to require structural coverage

Build evidence from full MarkRecord and PropagatedMarkRecord values, including payload. Call the evaluator with CoverageRequirement.StructureComplete. If the result is not complete, return no IfStructureLiftPayload and record rejected/missing evidence for diagnostics.

### Step 4: Change logical lifting to request logical reduction coverage

Use CoverageRequirement.LogicalReduction when finding removable operands. Preserve RemovableOperands and SurvivorOperands behavior, but do not allow a logical host fact to become a structural proof.

### Step 5: Run evaluator and focused behavior tests

    dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~CoverageDecisionConflictTests|FullyQualifiedName~DecisionComplexTests" --logger "console;verbosity=minimal"

Expected: topology-only cases no longer create an invalid structural payload; existing AND/OR contracts still pass through explicit proof-producing paths.

### Step 6: Commit the evaluator change

    git add src/NLISSN.Rules/Lift tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs
    git commit -m "fix: require typed evidence for structural coverage"

## 4. Add Lift-only structural proofs and consumed-node sets

**Files:**
- Modify: src/NLISSN.Core/Lifting/StructuralLiftPayloads.cs
- Modify: src/NLISSN.Core/Lifting/LiftedMarkRecord.cs
- Modify: src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs
- Modify: src/NLISSN.Rules/Lift/LogicalExpressionLiftingRule.cs
- Modify: src/NLISSN.Rules/Propose/Support/ProposalFacts.cs
- Test: tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs
- Test: tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs

### Step 1: Write proof-shape tests

Assert that:

- LogicalExpressionReductionPayload exposes host, removable operands, survivors, and consumed original keys;
- IfStructureLiftPayload exposes required condition keys, consumed keys, and a structural-completion proof kind;
- an if payload cannot be created from a topology host alone;
- custom operators or incomplete evidence produce no structural proof;
- the AND fixture creates a proof whose consumed keys include the logical host;
- the OR fixture creates a reduction proof but no StructureComplete proof.

### Step 2: Extend payloads without placing actions in Mark

Add immutable proof data to Lift payloads. Keep IfStructureLiftKind as the structural-result classification, but treat it as a conclusion backed by proof rather than a free-standing authorization. LogicalExpressionReductionPayload remains expression-level and must not set DominatesChildren.

### Step 3: Populate consumed keys in Lift

For if structures, include the anchor if, condition nodes, relevant tail/else nodes, and logical hosts consumed by the proof. For logical reductions, include the host and operands used to build the replacement. All keys must be from the original Roslyn tree.

### Step 4: Make Propose reject incomplete structural payloads

IfStructureProposalRule must verify the structural proof before calling DeleteDecisionFactory or a structural replacement factory. If the proof is missing, produce a diagnostic/skip result rather than a structural DecisionUnit.

### Step 5: Run proof tests

    dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageDecisionConflictTests|FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~DecisionStructureValidationTests" --logger "console;verbosity=minimal"

Expected: PASS, with AND and OR behavior explained by different proof kinds.

### Step 6: Commit the Lift proof model

    git add src/NLISSN.Core/Lifting src/NLISSN.Rules/Lift src/NLISSN.Rules/Propose/Support tests/NLISSN.HostTests
    git commit -m "feat: add explicit structural lift proofs"

## 5. Add DecisionFootprint to all proposal candidates

**Files:**
- Create: src/NLISSN.Core/Decision/DecisionFootprint.cs
- Modify: src/NLISSN.Core/Decision/DecisionModel.cs
- Modify: src/NLISSN.Core/Decision/DeleteDecisionFactory.cs
- Modify: src/NLISSN.Rules/Propose/Support/ProposalFacts.cs
- Modify: src/NLISSN.Rules/Propose/Support/ReplaceDecisionFactory.cs
- Modify: src/NLISSN.Rules/Propose/IfStructureProposalRule.cs
- Modify: src/NLISSN.Rules/Propose/LogicalExpressionProposalRule.cs
- Test: tests/NLISSN.UnitTests/Decision/
- Test: tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs

### Step 1: Write footprint construction tests

Assert that:

- every delete has an original anchor footprint;
- logical replacements consume only the original logical host and removable operands;
- structural if deletes consume the proof's consumed keys and declare DominatesChildren;
- structural replacements declare their consumed original nodes;
- replacement nodes outside the original tree do not enter ConsumedNodeKeys;
- legacy proposal factories fail validation or produce an explicit compatibility footprint rather than an empty one.

### Step 2: Implement immutable footprint types

Add EditComposition values: Independent, ComposesWithChildren, DominatesChildren, Exclusive. Add DecisionFootprint with AnchorNodeKey, ConsumedNodeKeys, Action, Composition, and ProofKind.

Keep ConflictKey and MergeKey for compatibility, but do not let them imply dominance. DecisionUnit should carry the footprint as immutable constructor state.

### Step 3: Populate footprints in factories

Update DeleteDecisionFactory, logical replacement construction, and if structure proposal helpers. The first fragment remains the original anchor. The replacement fragment is bound for rewrite only and is excluded from the original footprint.

### Step 4: Validate missing footprints

Extend decision binding/contract validation so a Delete or Replace without an original anchor footprint is invalid. A structural Delete without a StructureComplete proof must be rejected before resolver output.

### Step 5: Run footprint tests

    dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~Decision"
    dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageDecisionConflictTests|FullyQualifiedName~BindingValidatorTests"

Expected: all candidates produced by built-in proposal rules have non-empty source-tree-based footprints.

### Step 6: Commit proposal footprints

    git add src/NLISSN.Core/Decision src/NLISSN.Rules/Propose tests/NLISSN.UnitTests/Decision tests/NLISSN.HostTests/Decision tests/NLISSN.HostTests/Validation
    git commit -m "feat: attach edit footprints to decisions"

## 6. Replace span-based semantic filtering with one resolver

**Files:**
- Modify: src/NLISSN.Core/Decision/DecisionModel.cs
- Modify: src/NLISSN.Application/Analysis/ApplicationService.cs
- Modify: src/NLISSN.Core/Rewrite/PrototypeRewriter.cs
- Create or modify: src/NLISSN.Core/Decision/DecisionConflict.cs
- Modify: src/NLISSN.Core/Decision/AnalysisEvidence.cs
- Test: tests/NLISSN.HostTests/Decision/CoverageDecisionConflictTests.cs
- Test: tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs
- Test: tests/NLISSN.HostTests/Application/PipelineComponentTests.cs

### Step 1: Add resolver tests for each relationship

Cover:

- independent/non-overlapping: retain both;
- parent Delete + child Replace + valid dominance: retain parent only;
- parent Delete + child Replace without proof: reject parent or skip conflict domain; retain no candidate silently;
- parent Replace + child edit + consumed child: retain parent only;
- parent Replace + child edit without consumed child: conflict/skip;
- ancestor Delete + descendant Delete in one structural domain: retain ancestor;
- same anchor with different replacements: conflict/skip;
- partial overlap: conflict/skip;
- replacement node outside original tree: resolve using anchor/consumed keys only.

Each rejected candidate must be inspectable in returned conflict evidence.

### Step 2: Implement explicit footprint conflict classification

Build the conflict graph from original anchor/consumed keys and syntax ancestry only as a relation, not as authorization. Use explicit DominatesChildren and ComposesWithChildren to select a winner. If two candidates overlap without permitted composition, return a conflict result or Skip for the whole conflict domain.

### Step 3: Remove semantic behavior from FilterCompetingAncestors

Replace the exact-anchor Delete/Replace special case with resolver classification. It must not inspect only ReferenceEquals anchors or decide based on existence of any Replace candidate.

### Step 4: Remove semantic behavior from FilterCoveredDecisions

Do not drop a child merely because FinalNode.Span.Contains is true. The resolver must have already classified the relation. Keep a defensive assertion for resolver output whose source footprints still overlap illegally.

### Step 5: Remove the duplicate ApplicationService filter

Delete or bypass FilterNestedDeleteDecisions as a semantic resolver. ApplicationService should consume resolver output and only run unrelated safety checks such as local-reference preservation.

### Step 6: Keep Rewrite overlap checking defensive

PrototypeRewriter may refuse an illegal overlap and report it, but it must not choose a business winner that Decision did not prove. Use original anchors/footprints; never use replacement-node spans to infer source coverage.

### Step 7: Run resolver and rewrite tests

    dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CoverageDecisionConflictTests|FullyQualifiedName~DecisionComplexTests|FullyQualifiedName~PipelineComponentTests" --logger "console;verbosity=minimal"

Expected: explicit structural proof preserves existing AND outer-delete behavior; OR remains a local replacement; unsupported nested conflicts are observable and fail closed.

### Step 8: Commit the unified resolver

    git add src/NLISSN.Core/Decision src/NLISSN.Application/Analysis/ApplicationService.cs src/NLISSN.Core/Rewrite/PrototypeRewriter.cs tests/NLISSN.HostTests/Decision tests/NLISSN.HostTests/Application/PipelineComponentTests.cs
    git commit -m "fix: resolve nested edits by explicit dominance"

## 7. Add evidence diagnostics and full verification

**Files:**
- Modify: src/NLISSN.Core/Decision/AnalysisEvidence.cs
- Modify: src/NLISSN.Core/Validation/AnalysisValidationReport.cs
- Modify: src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs
- Modify: src/NLISSN.Core/Validation/DecisionBindingValidator.cs
- Test: tests/NLISSN.HostTests/Decision/DecisionEvidenceTests.cs
- Test: tests/NLISSN.HostTests/Validation/BindingValidatorTests.cs
- Test: tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs

### Step 1: Write diagnostics tests

Assert that evidence distinguishes:

- rejected topology host used for structural coverage;
- missing condition operand;
- parent candidate rejected for missing dominance proof;
- child candidate suppressed by an explicitly valid parent proof;
- partial overlap conflict;
- invalid legacy candidate with no footprint.

Diagnostics must include anchor key, child key when applicable, proof kind, and reason. No candidate should disappear without one of these outcomes.

### Step 2: Implement evidence recording

Record coverage results and resolver conflict outcomes in the existing analysis evidence graph. Extend AnalysisValidationReport only if the current validation issue shape cannot carry the stable reason. Keep evidence deterministic under parallel rule execution.

### Step 3: Run focused and full affected suites

    dotnet test .\tests\NLISSN.UnitTests\RoslynPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
    dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
    dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false

Expected: all existing contracts pass, including AND structural deletion, OR logical replacement, unrelated conflict domains, binding validation, and rewrite overlap tests.

### Step 4: Run repository verification

    pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
    git diff --check

### Step 5: Review the final diff and commit documentation updates

    git diff --stat
    git diff -- src/NLISSN.Core/Decision src/NLISSN.Rules/Lift src/NLISSN.Rules/Propose src/NLISSN.Application/Analysis
    git add docs/plans/2026-09-09-coverage-decision-constraints-design.md docs/plans/2026-09-09-coverage-decision-constraints-execution.md
    git commit -m "docs: plan coverage decision constraints"

Do not stage unrelated existing worktree changes.

## Definition of Done

- [ ] TopologyHost never satisfies StructureComplete by itself.
- [ ] Full propagation payload and provenance reach the coverage evaluator.
- [ ] StructureComplete is produced only by Lift proof with consumed original-node keys.
- [ ] Every Delete/Replace candidate carries an original-tree DecisionFootprint.
- [ ] Outer Delete dominates inner Replace only with explicit proof and consumed-anchor coverage.
- [ ] Nested candidates without dominance proof fail closed with observable conflict evidence.
- [ ] RuleDecisionEngine is the only semantic parent/child resolver.
- [ ] ApplicationService and Rewrite no longer make independent semantic winner choices.
- [ ] Existing AND/Delete and OR/Replace contracts pass.
- [ ] Unit, Host, Contract, harness consistency, and whitespace checks pass.
