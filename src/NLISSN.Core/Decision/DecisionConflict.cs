namespace NLISSN.Core.Decision;

public enum DecisionCandidateStatus
{
  Selected,
  Composed,
  Dominated,
  Rejected,
  Conflict,
  Unknown,
  Blocked
}

public enum DecisionRelationKind
{
  Requires,
  Dominates,
  Composes,
  Conflicts,
  MemberOfAtomicGroup
}

public sealed record DecisionRelation(
  string FromCandidateId,
  string ToCandidateId,
  DecisionRelationKind Kind,
  string Reason,
  IReadOnlyList<string>? ProofReferences = null)
{
  public IReadOnlyList<string> ProofReferences { get; init; } =
    (ProofReferences ?? Array.Empty<string>()).ToArray();
}

public sealed record DecisionCandidateResult(
  string CandidateId,
  DecisionCandidateStatus Status,
  DecisionUnit Candidate,
  string Reason,
  IReadOnlyList<DecisionRelation>? Relations = null)
{
  public IReadOnlyList<DecisionRelation> Relations { get; init; } =
    (Relations ?? Array.Empty<DecisionRelation>()).ToArray();

  public EditIntent Intent => Candidate.Intent;
}
