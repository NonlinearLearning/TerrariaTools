using Microsoft.CodeAnalysis;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Lifting;

public enum CoverageGoal
{
  AtomicTarget = 0,
  LogicalReduction = 1,
  ExpressionReplacement = 2,
  StructureComplete = 3,
  DeclarationSync = 4,
  CallsiteCompleteness = 5,
  EffectPreservingRemoval = 6,
  AtomicTransaction = 7
}

public enum CoverageProofStatus
{
  Complete = 0,
  Partial = 1,
  Unknown = 2,
  Rejected = 3
}

public enum CoverageRequirement
{
  ExactAnchor = 0,
  AtomicEvidence = 1,
  TopologyHost = 2,
  RemovableOperands = 3,
  SurvivorOperands = 4,
  ReplacementEvidence = 5,
  StructureEvidence = 6,
  ControlFlow = 7,
  EffectObligations = 8,
  Binding = 9,
  CompleteQuery = 10,
  AtomicMembers = 11,
  SourceVersion = 12
}

public sealed record PreservedObligation(
  string Kind,
  string Description,
  string? Identity = null);

/// <summary>
/// A goal-specific proof. Its status is never inferred from a display reason
/// or from a syntax span relationship.
/// </summary>
public sealed record CoverageProof
{
  public CoverageProof(
    CoverageGoal goal,
    CoverageProofStatus status,
    IReadOnlyList<CoverageEvidence>? acceptedEvidence = null,
    IReadOnlyList<CoverageEvidence>? rejectedEvidence = null,
    IReadOnlyList<CoverageRequirement>? missingRequirements = null,
    IReadOnlySet<string>? consumedNodeKeys = null,
    IReadOnlyList<PreservedObligation>? preservedObligations = null,
    IReadOnlyList<CoverageProof>? dependencies = null,
    string? diagnostic = null)
  {
    Goal = goal;
    Status = status;
    AcceptedEvidence = (acceptedEvidence ?? Array.Empty<CoverageEvidence>()).ToArray();
    RejectedEvidence = (rejectedEvidence ?? Array.Empty<CoverageEvidence>()).ToArray();
    MissingRequirements = (missingRequirements ?? Array.Empty<CoverageRequirement>()).ToArray();
    ConsumedNodeKeys = new HashSet<string>(
      consumedNodeKeys is null ? Array.Empty<string>() : consumedNodeKeys,
      StringComparer.Ordinal);
    PreservedObligations = (preservedObligations ?? Array.Empty<PreservedObligation>()).ToArray();
    Dependencies = (dependencies ?? Array.Empty<CoverageProof>()).ToArray();
    Diagnostic = diagnostic;
  }

  public CoverageGoal Goal { get; }

  public CoverageProofStatus Status { get; }

  public IReadOnlyList<CoverageEvidence> AcceptedEvidence { get; }

  public IReadOnlyList<CoverageEvidence> RejectedEvidence { get; }

  public IReadOnlyList<CoverageRequirement> MissingRequirements { get; }

  public IReadOnlySet<string> ConsumedNodeKeys { get; }

  public IReadOnlyList<PreservedObligation> PreservedObligations { get; }

  public IReadOnlyList<CoverageProof> Dependencies { get; }

  public string? Diagnostic { get; }

  public bool IsComplete => Status == CoverageProofStatus.Complete;
}

/// <summary>
/// Requirement-aware evidence evaluator used by Lift and authorization seams.
/// </summary>
public static class CoverageProofEvaluator
{
  public static CoverageProof Evaluate(
    CoverageGoal goal,
    SyntaxNode target,
    IEnumerable<CoverageEvidence> evidence)
  {
    ArgumentNullException.ThrowIfNull(target);
    ArgumentNullException.ThrowIfNull(evidence);

    var allEvidence = evidence.ToArray();
    var targetKey = FactIdentity.BuildNodeKey(target);
    var exact = allEvidence
      .Where(candidate => string.Equals(candidate.AnchorNodeKey, targetKey, StringComparison.Ordinal))
      .ToArray();
    var uncertain = exact.Where(candidate => !candidate.IsAvailable).ToArray();
    var available = exact.Where(candidate => candidate.IsAvailable).ToArray();
    var consumed = available.Select(candidate => candidate.AnchorNodeKey).ToHashSet(StringComparer.Ordinal);

    if (uncertain.Length > 0)
    {
      return Create(
        goal,
        CoverageProofStatus.Unknown,
        available,
        uncertain,
        new[] { CoverageRequirement.CompleteQuery },
        consumed,
        "Evidence certainty is not Available.");
    }

    return goal switch
    {
      CoverageGoal.AtomicTarget => EvaluateAtomicTarget(goal, available, consumed),
      CoverageGoal.StructureComplete => EvaluateStructure(goal, available, consumed),
      CoverageGoal.LogicalReduction => EvaluateLogicalReduction(goal, target, allEvidence, available, consumed),
      CoverageGoal.ExpressionReplacement => EvaluateExpressionReplacement(goal, available, consumed),
      _ => EvaluateGeneral(goal, available, consumed)
    };
  }

  private static CoverageProof EvaluateAtomicTarget(
    CoverageGoal goal,
    IReadOnlyList<CoverageEvidence> exact,
    IReadOnlySet<string> consumed)
  {
    var accepted = exact.Where(candidate => candidate.Capability is FactCapability.AtomicTarget or FactCapability.GlobalTarget).ToArray();
    var rejected = exact.Except(accepted).ToArray();
    return accepted.Length > 0
      ? Create(goal, CoverageProofStatus.Complete, accepted, rejected, Array.Empty<CoverageRequirement>(), consumed)
      : Create(
        goal,
        CoverageProofStatus.Rejected,
        Array.Empty<CoverageEvidence>(),
        rejected,
        new[] { CoverageRequirement.AtomicEvidence },
        consumed,
        "An atomic target requires an exact AtomicTarget or GlobalTarget fact.");
  }

  private static CoverageProof EvaluateStructure(
    CoverageGoal goal,
    IReadOnlyList<CoverageEvidence> exact,
    IReadOnlySet<string> consumed)
  {
    var topology = exact.Where(candidate => candidate.Capability == FactCapability.TopologyHost).ToArray();
    var structureEvidence = exact.Where(IsLiftOwnedStructureEvidence).ToArray();
    var accepted = structureEvidence;
    var rejected = exact.Except(accepted).ToArray();
    if (accepted.Length > 0)
    {
      return Create(goal, CoverageProofStatus.Complete, accepted, rejected, Array.Empty<CoverageRequirement>(), consumed);
    }

    return Create(
      goal,
      topology.Length > 0 ? CoverageProofStatus.Rejected : CoverageProofStatus.Rejected,
      Array.Empty<CoverageEvidence>(),
      rejected,
      new[] { CoverageRequirement.StructureEvidence, CoverageRequirement.ControlFlow },
      consumed,
      topology.Length > 0
        ? "TopologyHost evidence describes an expression host, not structural completeness."
        : "No Lift-owned structure proof was supplied.");
  }

  private static CoverageProof EvaluateLogicalReduction(
    CoverageGoal goal,
    SyntaxNode target,
    IReadOnlyList<CoverageEvidence> allEvidence,
    IReadOnlyList<CoverageEvidence> exact,
    IReadOnlySet<string> consumed)
  {
    var host = exact.Where(candidate => candidate.Capability == FactCapability.TopologyHost).ToArray();
    var childEvidence = allEvidence.Where(candidate =>
      candidate.IsAvailable &&
      candidate.Mark.SyntaxNode.AncestorsAndSelf().Any(ancestor => ReferenceEquals(ancestor, target)) &&
      candidate.Capability is FactCapability.AtomicTarget or FactCapability.ChildComposable).ToArray();
    var accepted = host.Concat(childEvidence).DistinctBy(candidate => candidate.Identity).ToArray();
    var missing = new List<CoverageRequirement>();
    if (host.Length == 0) missing.Add(CoverageRequirement.TopologyHost);
    if (childEvidence.Length == 0) missing.Add(CoverageRequirement.RemovableOperands);
    return missing.Count == 0
      ? Create(goal, CoverageProofStatus.Complete, accepted, exact.Except(accepted).ToArray(), missing, consumed)
      : Create(goal, host.Length == 0 ? CoverageProofStatus.Rejected : CoverageProofStatus.Partial, accepted, exact.Except(accepted).ToArray(), missing, consumed);
  }

  private static CoverageProof EvaluateExpressionReplacement(
    CoverageGoal goal,
    IReadOnlyList<CoverageEvidence> exact,
    IReadOnlySet<string> consumed)
  {
    var accepted = exact.Where(candidate => candidate.Capability is FactCapability.AtomicTarget or FactCapability.ChildComposable).ToArray();
    return accepted.Length > 0
      ? Create(goal, CoverageProofStatus.Complete, accepted, exact.Except(accepted).ToArray(), Array.Empty<CoverageRequirement>(), consumed)
      : Create(goal, CoverageProofStatus.Rejected, Array.Empty<CoverageEvidence>(), exact, new[] { CoverageRequirement.ReplacementEvidence }, consumed);
  }

  private static CoverageProof EvaluateGeneral(
    CoverageGoal goal,
    IReadOnlyList<CoverageEvidence> exact,
    IReadOnlySet<string> consumed)
  {
    return exact.Count == 0
      ? Create(goal, CoverageProofStatus.Rejected, exact, exact, new[] { CoverageRequirement.ExactAnchor }, consumed)
      : Create(goal, CoverageProofStatus.Partial, exact, Array.Empty<CoverageEvidence>(), new[] { CoverageRequirement.Binding }, consumed);
  }

  private static bool IsLiftOwnedStructureEvidence(CoverageEvidence evidence)
  {
    return evidence.Mark.FactKind is RuleFactKind.LiftIfStructure or RuleFactKind.LiftSwitchStructure or RuleFactKind.LiftControlStructure;
  }

  private static CoverageProof Create(
    CoverageGoal goal,
    CoverageProofStatus status,
    IReadOnlyList<CoverageEvidence> accepted,
    IReadOnlyList<CoverageEvidence> rejected,
    IReadOnlyList<CoverageRequirement> missing,
    IReadOnlySet<string> consumed,
    string? diagnostic = null)
  {
    return new CoverageProof(goal, status, accepted, rejected, missing, consumed, diagnostic: diagnostic);
  }
}
