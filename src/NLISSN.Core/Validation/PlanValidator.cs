using Microsoft.CodeAnalysis;
using NLISSN.Core.Decision;

namespace NLISSN.Core.Validation;

/// <summary>
/// Converts a planner result into an executable authority token only after
/// selected candidates, relations, bindings and original-tree footprints have
/// all passed fail-closed validation.
/// </summary>
public sealed class PlanValidator
{
  private readonly CandidateValidator _candidateValidator;

  public PlanValidator(CandidateValidator? candidateValidator = null)
  {
    _candidateValidator = candidateValidator ?? new CandidateValidator();
  }

  public bool TryCreateExecutablePlan(
    DecisionPlan decisionPlan,
    SyntaxNode compilationRoot,
    out ExecutablePlan? executablePlan,
    out AnalysisValidationReport report)
  {
    ArgumentNullException.ThrowIfNull(decisionPlan);
    ArgumentNullException.ThrowIfNull(compilationRoot);

    var issues = new List<ValidationIssue>();
    if (!decisionPlan.IsExecutable)
    {
      issues.Add(Issue(
        "PLAN001",
        "plan",
        "The decision plan contains Unknown or Blocked candidates and cannot be executed."));
    }

    var selected = decisionPlan.Candidates
      .Where(candidate => candidate.Status is DecisionCandidateStatus.Selected or DecisionCandidateStatus.Composed)
      .ToArray();
    foreach (var candidate in selected)
    {
      ValidateSelectedCandidate(candidate, compilationRoot, issues);
    }

    ValidateRelations(decisionPlan, issues);
    ValidateSelectedFootprints(selected, issues);

    report = AnalysisValidationReport.Create(issues);
    if (!report.IsValid)
    {
      executablePlan = null;
      return false;
    }

    executablePlan = new ExecutablePlan(
      decisionPlan,
      new DecisionPlanner().ResolveDecisions(decisionPlan));
    return true;
  }

  public AnalysisValidationReport Validate(
    DecisionPlan decisionPlan,
    SyntaxNode compilationRoot)
  {
    TryCreateExecutablePlan(decisionPlan, compilationRoot, out _, out var report);
    return report;
  }

  private void ValidateSelectedCandidate(
    DecisionCandidateResult candidate,
    SyntaxNode compilationRoot,
    ICollection<ValidationIssue> issues)
  {
    var intent = candidate.Intent;
    if (!_candidateValidator.Validate(intent).IsComplete)
    {
      issues.Add(Issue(
        "PLAN001",
        candidate.CandidateId,
        "Selected candidate does not carry a complete authorization intent."));
    }

    if (!IsInOriginalTree(compilationRoot, intent.Anchor))
    {
      issues.Add(Issue(
        "PLAN002",
        candidate.CandidateId,
        "Selected candidate anchor is outside the compilation root."));
    }

    if (intent.Action is DecisionActionKind.Delete or DecisionActionKind.Replace)
    {
      if (intent.ProofReferences.Count == 0)
      {
        issues.Add(Issue(
          "PLAN003",
          candidate.CandidateId,
          "Destructive candidate has no typed proof reference."));
      }

      if (intent.EraseSet.Count == 0)
      {
        issues.Add(Issue(
          "PLAN004",
          candidate.CandidateId,
          "Destructive candidate has an empty original-tree EraseSet."));
      }

      if (string.IsNullOrWhiteSpace(intent.SourceVersion))
      {
        issues.Add(Issue(
          "PLAN005",
          candidate.CandidateId,
          "Destructive candidate has no source version."));
      }
    }

    if (intent.Action == DecisionActionKind.Replace &&
        candidate.Candidate.Fragments.All(fragment =>
          !string.Equals(fragment.Name, "replacement", StringComparison.Ordinal)))
    {
      issues.Add(Issue(
        "PLAN006",
        candidate.CandidateId,
        "Replace candidate has no replacement fragment binding."));
    }
  }

  private static void ValidateRelations(
    DecisionPlan decisionPlan,
    ICollection<ValidationIssue> issues)
  {
    var candidateIds = decisionPlan.Candidates
      .Select(candidate => candidate.CandidateId)
      .ToHashSet(StringComparer.Ordinal);
    foreach (var relation in decisionPlan.Relations)
    {
      var fromIsCandidate = candidateIds.Contains(relation.FromCandidateId);
      var toIsCandidate = candidateIds.Contains(relation.ToCandidateId);
      var isAtomicGroupEdge = relation.Kind == DecisionRelationKind.MemberOfAtomicGroup;
      if ((!fromIsCandidate || !toIsCandidate) && !isAtomicGroupEdge)
      {
        issues.Add(Issue(
          "PLAN007",
          $"{relation.FromCandidateId}:{relation.ToCandidateId}",
          "Decision relation references an unknown candidate."));
      }
    }

    foreach (var composed in decisionPlan.Candidates.Where(candidate =>
               candidate.Status == DecisionCandidateStatus.Composed))
    {
      var hasComposition = composed.Relations.Any(relation =>
        relation.Kind == DecisionRelationKind.Composes);
      if (!hasComposition)
      {
        issues.Add(Issue(
          "PLAN008",
          composed.CandidateId,
          "Composed candidate has no residual composition relation."));
      }
    }
  }

  private static void ValidateSelectedFootprints(
    IReadOnlyList<DecisionCandidateResult> selected,
    ICollection<ValidationIssue> issues)
  {
    for (var index = 0; index < selected.Count; index++)
    {
      for (var otherIndex = index + 1; otherIndex < selected.Count; otherIndex++)
      {
        var left = selected[index];
        var right = selected[otherIndex];
        if (left.Intent.AtomicGroup is not null &&
            string.Equals(left.Intent.AtomicGroup, right.Intent.AtomicGroup, StringComparison.Ordinal))
        {
          continue;
        }

        if (left.Intent.EraseSet.Intersect(right.Intent.EraseSet, StringComparer.Ordinal).Any())
        {
          issues.Add(Issue(
            "PLAN009",
            $"{left.CandidateId}:{right.CandidateId}",
            "Selected original-tree footprints overlap without an executable composition relation."));
        }
      }
    }
  }

  private static bool IsInOriginalTree(SyntaxNode root, SyntaxNode node)
  {
    return ReferenceEquals(root.SyntaxTree, node.SyntaxTree) &&
      (ReferenceEquals(root, node) || node.AncestorsAndSelf().Any(candidate => ReferenceEquals(candidate, root)));
  }

  private static ValidationIssue Issue(string code, string key, string message)
  {
    return new ValidationIssue(
      code,
      ValidationSeverity.Error,
      $"{code}:{key}",
      message);
  }
}
