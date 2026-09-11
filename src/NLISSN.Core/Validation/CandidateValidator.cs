using Microsoft.CodeAnalysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Validation;

/// <summary>
/// Re-checks stage boundaries before a proposal can become an authorized
/// intent. Public records are treated as untrusted values at every boundary.
/// </summary>
public sealed class CandidateValidator
{
  public CandidateAuthorization Authorize(
    MarkRecord rawMark,
    SyntaxNode target,
    DecisionActionKind action)
  {
    ArgumentNullException.ThrowIfNull(rawMark);
    ArgumentNullException.ThrowIfNull(target);

    return CandidateAuthorization.Rejected(
      BuildCandidateId(rawMark.RuleId, target, action),
      target,
      action,
      "A raw MarkRecord is an observation and cannot authorize a destructive edit.");
  }

  public CandidateAuthorization Validate(LiftedMarkRecord liftedMark)
  {
    ArgumentNullException.ThrowIfNull(liftedMark);
    var target = ResolveTarget(liftedMark);
    if (target is null)
    {
      return CandidateAuthorization.Rejected(
        BuildCandidateId(liftedMark.RuleId, liftedMark.Mark.SyntaxNode, DecisionActionKind.Delete),
        liftedMark.Mark.SyntaxNode,
        DecisionActionKind.Delete,
        "The lifted record has no structural target payload.");
    }

    if (!IsStructuralFact(liftedMark))
    {
      return CandidateAuthorization.Rejected(
        BuildCandidateId(liftedMark.RuleId, target, DecisionActionKind.Delete),
        target,
        DecisionActionKind.Delete,
        "A lifted structural authorization requires a matching Lift fact kind and structure payload.");
    }

    var proof = ResolveProof(liftedMark);
    if (proof is null ||
        proof.Goal != CoverageGoal.StructureComplete ||
        proof.Status != CoverageProofStatus.Complete)
    {
      return CandidateAuthorization.Rejected(
        BuildCandidateId(liftedMark.RuleId, target, DecisionActionKind.Delete),
        target,
        DecisionActionKind.Delete,
        "A structural candidate requires a complete StructureComplete proof.");
    }

    if (proof.AcceptedEvidence.Count == 0 || !ContainsTarget(proof, target))
    {
      return CandidateAuthorization.Rejected(
        BuildCandidateId(liftedMark.RuleId, target, DecisionActionKind.Delete),
        target,
        DecisionActionKind.Delete,
        "The structural proof has no accepted evidence for its target region.");
    }

    var proofReference = BuildProofReference(proof, target);
    var intent = EditIntent.Create(
      BuildCandidateId(liftedMark.RuleId, target, DecisionActionKind.Delete),
      target,
      DecisionActionKind.Delete,
      eraseNodeKeys: proof.ConsumedNodeKeys,
      proofReferences: new[] { proofReference },
      behaviorBudget: ToBehaviorBudget(proof),
      composition: DecisionComposition.OpaqueDominates,
      status: EditIntentStatus.Complete,
      dominatesChildren: true);
    return new CandidateAuthorization(
      intent.CandidateId,
      target,
      DecisionActionKind.Delete,
      EditIntentStatus.Complete,
      "Lift-owned StructureComplete proof authorizes the structural delete.",
      proofReferences: intent.ProofReferences,
      readSet: intent.ReadSet,
      eraseSet: intent.EraseSet,
      writeSet: intent.WriteSet,
      preserveSet: intent.PreserveSet,
      behaviorBudget: intent.BehaviorBudget,
      composition: intent.Composition,
      sourceVersion: intent.SourceVersion,
      intent: intent);
  }

  public CandidateAuthorization Validate(EditIntent intent)
  {
    ArgumentNullException.ThrowIfNull(intent);
    if (intent.Action is DecisionActionKind.Delete or DecisionActionKind.Replace &&
        intent.Status == EditIntentStatus.Complete &&
        intent.ProofReferences.Count > 0 &&
        intent.EraseSet.Count > 0 &&
        !string.IsNullOrWhiteSpace(intent.SourceVersion))
    {
      return new CandidateAuthorization(
        intent.CandidateId,
        intent.Anchor,
        intent.Action,
        EditIntentStatus.Complete,
        "Intent contains the required proof, footprint and source version.",
        allowedActions: new[] { intent.Action },
        proofReferences: intent.ProofReferences,
        readSet: intent.ReadSet,
        eraseSet: intent.EraseSet,
        writeSet: intent.WriteSet,
        preserveSet: intent.PreserveSet,
        behaviorBudget: intent.BehaviorBudget,
        composition: intent.Composition,
        residualMapping: intent.ResidualMapping,
        atomicGroup: intent.AtomicGroup,
        sourceVersion: intent.SourceVersion,
        intent: intent);
    }

    return CandidateAuthorization.Rejected(
      intent.CandidateId,
      intent.Anchor,
      intent.Action,
      "Destructive intent is missing a complete proof, original-tree footprint or source version.");
  }

  public CandidateAuthorization Validate(DecisionUnit candidate)
  {
    ArgumentNullException.ThrowIfNull(candidate);
    var authorization = Validate(candidate.Intent);
    if (!authorization.IsComplete)
    {
      return authorization;
    }

    if (candidate.Action != candidate.Intent.Action ||
        !string.Equals(candidate.RuleId, candidate.Intent.CandidateId, StringComparison.Ordinal) &&
        string.IsNullOrWhiteSpace(candidate.Intent.CandidateId))
    {
      return CandidateAuthorization.Rejected(
        candidate.Intent.CandidateId,
        candidate.Intent.Anchor,
        candidate.Action,
        "Decision unit action and authorized intent do not agree.");
    }

    return authorization;
  }

  private static SyntaxNode? ResolveTarget(LiftedMarkRecord liftedMark)
  {
    return liftedMark.Payload switch
    {
      IfStructureLiftPayload payload => payload.AnchorIf,
      ControlStructureLiftPayload payload => payload.Anchor,
      SwitchStructureLiftPayload payload => payload.Anchor,
      _ => null
    };
  }

  private static CoverageProof? ResolveProof(LiftedMarkRecord liftedMark)
  {
    return liftedMark.Payload switch
    {
      IfStructureLiftPayload payload => payload.Proof,
      ControlStructureLiftPayload payload => payload.Proof,
      SwitchStructureLiftPayload payload => payload.Proof,
      _ => null
    };
  }

  private static bool IsStructuralFact(LiftedMarkRecord liftedMark)
  {
    return liftedMark.StructureKind switch
    {
      StructuralKind.If => liftedMark.Mark.FactKind == RuleFactKind.LiftIfStructure &&
        liftedMark.Payload is IfStructureLiftPayload,
      StructuralKind.Loop or StructuralKind.Return => liftedMark.Mark.FactKind == RuleFactKind.LiftControlStructure &&
        liftedMark.Payload is ControlStructureLiftPayload,
      StructuralKind.Switch => liftedMark.Mark.FactKind == RuleFactKind.LiftSwitchStructure &&
        liftedMark.Payload is SwitchStructureLiftPayload,
      _ => false
    };
  }

  private static bool ContainsTarget(CoverageProof proof, SyntaxNode target)
  {
    var factKey = FactIdentity.BuildNodeKey(target);
    var decisionKey = DecisionCpgFactory.BuildNodeKey(target);
    return proof.ConsumedNodeKeys.Contains(factKey) ||
      proof.ConsumedNodeKeys.Contains(decisionKey) ||
      proof.AcceptedEvidence.Any(evidence =>
        string.Equals(evidence.AnchorNodeKey, factKey, StringComparison.Ordinal) ||
        ReferenceEquals(evidence.Mark.SyntaxNode, target));
  }

  private static string BuildProofReference(CoverageProof proof, SyntaxNode target)
  {
    return $"proof:{proof.Goal}:{FactIdentity.BuildNodeKey(target)}";
  }

  private static BehaviorBudget ToBehaviorBudget(CoverageProof proof)
  {
    var obligations = proof.PreservedObligations
      .Select(obligation => Enum.TryParse<BehaviorObligationKind>(obligation.Kind, true, out var kind)
        ? (BehaviorObligationKind?)kind
        : null)
      .Where(kind => kind is not null)
      .Select(kind => kind!.Value)
      .ToArray();
    return new BehaviorBudget(obligations);
  }

  private static string BuildCandidateId(
    string ruleId,
    SyntaxNode target,
    DecisionActionKind action)
  {
    return $"candidate:{ruleId}:{DecisionCpgFactory.BuildNodeKey(target)}:{action}";
  }
}
