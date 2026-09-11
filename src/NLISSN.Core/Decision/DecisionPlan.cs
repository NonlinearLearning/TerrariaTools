namespace NLISSN.Core.Decision;

/// <summary>
/// Deterministic result of resolving all candidate relationships in one epoch.
/// Rejected and dominated candidates remain visible for diagnostics.
/// </summary>
public sealed record DecisionPlan
{
  public DecisionPlan(
    IReadOnlyList<DecisionCandidateResult> candidates,
    IReadOnlyList<DecisionRelation>? relations = null,
    IReadOnlyList<string>? diagnostics = null)
  {
    Candidates = candidates
      .OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
      .ToArray();
    Relations = (relations ?? Array.Empty<DecisionRelation>())
      .OrderBy(relation => relation.FromCandidateId, StringComparer.Ordinal)
      .ThenBy(relation => relation.ToCandidateId, StringComparer.Ordinal)
      .ThenBy(relation => relation.Kind)
      .ToArray();
    Diagnostics = (diagnostics ?? Array.Empty<string>()).Order(StringComparer.Ordinal).ToArray();
  }

  public IReadOnlyList<DecisionCandidateResult> Candidates { get; }

  public IReadOnlyList<DecisionRelation> Relations { get; }

  public IReadOnlyList<string> Diagnostics { get; }

  public IReadOnlyList<DecisionUnit> SelectedUnits => Candidates
    .Where(candidate => candidate.Status is DecisionCandidateStatus.Selected or DecisionCandidateStatus.Composed)
    .Select(candidate => candidate.Candidate)
    .ToArray();

  public DecisionCandidateResult Get(string candidateId)
  {
    return Candidates.Single(candidate =>
      string.Equals(candidate.CandidateId, candidateId, StringComparison.Ordinal));
  }

  public bool IsExecutable => Candidates.All(candidate => candidate.Status is
    DecisionCandidateStatus.Selected or
    DecisionCandidateStatus.Composed or
    DecisionCandidateStatus.Dominated or
    DecisionCandidateStatus.Rejected or
    DecisionCandidateStatus.Conflict or
    DecisionCandidateStatus.Unknown or
    DecisionCandidateStatus.Blocked) &&
    !Candidates.Any(candidate => candidate.Status is DecisionCandidateStatus.Unknown or DecisionCandidateStatus.Blocked);
}
