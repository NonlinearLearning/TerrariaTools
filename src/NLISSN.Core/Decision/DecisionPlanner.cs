using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Decision;

/// <summary>
/// The single semantic resolver for candidate composition, dominance and
/// conflicts. It never infers authority from span size or generation order.
/// </summary>
public sealed class DecisionPlanner
{
  public DecisionPlan Plan(IReadOnlyList<DecisionUnit> candidates)
  {
    ArgumentNullException.ThrowIfNull(candidates);
    var ordered = candidates
      .OrderBy(candidate => candidate.Intent.CandidateId, StringComparer.Ordinal)
      .ThenBy(candidate => candidate.RuleId, StringComparer.Ordinal)
      .ToArray();
    var results = ordered.ToDictionary(
      candidate => candidate.Intent.CandidateId,
      candidate => new MutableResult(candidate),
      StringComparer.Ordinal);
    var relations = new List<DecisionRelation>();

    foreach (var group in ordered
      .Where(candidate => candidate.Intent.AtomicGroup is not null)
      .GroupBy(candidate => candidate.Intent.AtomicGroup!, StringComparer.Ordinal))
    {
      if (group.Any(candidate => candidate.Intent.Status != EditIntentStatus.Complete))
      {
        foreach (var member in group)
        {
          results[member.Intent.CandidateId].Set(
            DecisionCandidateStatus.Blocked,
            "Atomic group contains Unknown or Rejected intent.");
        }

        AddAtomicRelations(group, relations, "Atomic group is blocked because at least one member is incomplete.");
      }
    }

    foreach (var candidate in ordered)
    {
      var result = results[candidate.Intent.CandidateId];
      if (candidate.Intent.Status == EditIntentStatus.Unknown)
      {
        result.Set(DecisionCandidateStatus.Unknown, "Candidate intent is Unknown.");
      }
      else if (candidate.Intent.Status == EditIntentStatus.Rejected)
      {
        result.Set(DecisionCandidateStatus.Rejected, "Candidate intent was rejected before planning.");
      }
    }

    for (var index = 0; index < ordered.Length; index++)
    {
      for (var otherIndex = index + 1; otherIndex < ordered.Length; otherIndex++)
      {
        var left = ordered[index];
        var right = ordered[otherIndex];
        if (left.Intent.AtomicGroup is not null &&
            string.Equals(left.Intent.AtomicGroup, right.Intent.AtomicGroup, StringComparison.Ordinal))
        {
          continue;
        }

        ResolvePair(left, right, results, relations);
      }
    }

    foreach (var candidate in ordered)
    {
      var result = results[candidate.Intent.CandidateId];
      if (result.Status is null)
      {
        result.Set(DecisionCandidateStatus.Selected, "Candidate has no conflicting relationship.");
      }
    }

    var projected = results.Values
      .Select(result => result.Build(relations))
      .ToArray();
    return new DecisionPlan(projected, relations);
  }

  public DecisionPlan Plan(IEnumerable<EditIntent> intents)
  {
    ArgumentNullException.ThrowIfNull(intents);
    var candidates = intents.Select(CreateCompatibilityUnit).ToArray();
    return Plan(candidates);
  }

  public IReadOnlyList<RuleDecision> ResolveDecisions(DecisionPlan plan)
  {
    ArgumentNullException.ThrowIfNull(plan);
    return plan.Candidates
      .Where(candidate => candidate.Status is DecisionCandidateStatus.Selected or DecisionCandidateStatus.Composed)
      .Select(candidate => ToRuleDecision(candidate.Candidate))
      .OrderBy(decision => decision.FinalNode.SyntaxTree?.FilePath, StringComparer.Ordinal)
      .ThenBy(decision => decision.FinalNode.SpanStart)
      .ToArray();
  }

  private static void ResolvePair(
    DecisionUnit left,
    DecisionUnit right,
    IDictionary<string, MutableResult> results,
    ICollection<DecisionRelation> relations)
  {
    var leftIntent = left.Intent;
    var rightIntent = right.Intent;
    if (leftIntent.Status != EditIntentStatus.Complete || rightIntent.Status != EditIntentStatus.Complete)
    {
      return;
    }

    if (string.Equals(leftIntent.AnchorNodeKey, rightIntent.AnchorNodeKey, StringComparison.Ordinal))
    {
      AddConflict(left, right, results, relations, "Candidates share an anchor but propose incompatible edits.");
      return;
    }

    var leftContainsRight = IsAncestor(left, right);
    var rightContainsLeft = IsAncestor(right, left);
    if (leftContainsRight || rightContainsLeft)
    {
      var parent = leftContainsRight ? left : right;
      var child = leftContainsRight ? right : left;
      ResolveNested(parent, child, results, relations);
      return;
    }

    var overlap = leftIntent.EraseSet.Intersect(rightIntent.EraseSet, StringComparer.Ordinal).Any();
    if (overlap)
    {
      AddConflict(left, right, results, relations, "Original-tree footprints partially overlap without a composition proof.");
    }
  }

  private static void ResolveNested(
    DecisionUnit parent,
    DecisionUnit child,
    IDictionary<string, MutableResult> results,
    ICollection<DecisionRelation> relations)
  {
    var parentIntent = parent.Intent;
    var childIntent = child.Intent;
    if (parentIntent.Action == DecisionActionKind.Delete)
    {
      var canDominate = parentIntent.DominatesChildren &&
        parentIntent.Composition == DecisionComposition.OpaqueDominates &&
        (parentIntent.HasStructureCompleteProof ||
         parentIntent.HasCompleteDeclarationBoundaryProof) &&
        parentIntent.Erases(childIntent.AnchorNodeKey) &&
        parentIntent.IsPreserveCompatibleWith(childIntent);
      if (canDominate)
      {
        results[parentIntent.CandidateId].Set(
          DecisionCandidateStatus.Selected,
          parentIntent.HasStructureCompleteProof
            ? "Parent Delete has explicit StructureComplete dominance proof."
            : "Parent Delete has explicit declaration-boundary dominance proof.");
        results[childIntent.CandidateId].Set(
          DecisionCandidateStatus.Dominated,
          "Child is consumed by the parent's proven EraseSet.");
        relations.Add(new DecisionRelation(
          parentIntent.CandidateId,
          childIntent.CandidateId,
          DecisionRelationKind.Dominates,
          "Parent Delete explicitly dominates child.",
          parentIntent.ProofReferences.ToArray()));
        return;
      }

      results[parentIntent.CandidateId].Set(
        DecisionCandidateStatus.Rejected,
        "Parent Delete lacks explicit dominance proof, EraseSet coverage, or preserve compatibility.");
      relations.Add(new DecisionRelation(
        parentIntent.CandidateId,
        childIntent.CandidateId,
        DecisionRelationKind.Conflicts,
        "Parent Delete cannot silently consume a child candidate."));
      return;
    }

    if (parentIntent.Action == DecisionActionKind.Replace &&
        parentIntent.Composition == DecisionComposition.Composable &&
        parentIntent.ResidualMapping is not null)
    {
      var mapping = parentIntent.ResidualMapping.TryMap(childIntent.AnchorNodeKey, out var residual)
        ? residual
        : null;
      if (mapping is { Kind: ResidualMappingKind.Retained or ResidualMappingKind.Replaced } &&
          parentIntent.IsPreserveCompatibleWith(childIntent))
      {
        results[parentIntent.CandidateId].Set(
          DecisionCandidateStatus.Composed,
          "Parent Replace retains a mapped child region.");
        results[childIntent.CandidateId].Set(
          DecisionCandidateStatus.Composed,
          "Child is composed through the parent's residual mapping.");
        relations.Add(new DecisionRelation(
          parentIntent.CandidateId,
          childIntent.CandidateId,
          DecisionRelationKind.Composes,
          "Residual mapping retains the child candidate.",
          parentIntent.ProofReferences.ToArray()));
        return;
      }

      if (mapping is { Kind: ResidualMappingKind.Removed } &&
          parentIntent.Erases(childIntent.AnchorNodeKey) &&
          parentIntent.IsPreserveCompatibleWith(childIntent))
      {
        results[parentIntent.CandidateId].Set(
          DecisionCandidateStatus.Selected,
          "Parent Replace explicitly removes the child through its residual mapping.");
        results[childIntent.CandidateId].Set(
          DecisionCandidateStatus.Dominated,
          "Child is removed by the parent replacement's proven residual mapping.");
        relations.Add(new DecisionRelation(
          parentIntent.CandidateId,
          childIntent.CandidateId,
          DecisionRelationKind.Dominates,
          "Parent Replace removes the child candidate through residual mapping.",
          parentIntent.ProofReferences.ToArray()));
        return;
      }

      if (mapping is { Kind: ResidualMappingKind.Unknown })
      {
        results[parentIntent.CandidateId].Set(
          DecisionCandidateStatus.Unknown,
          mapping.Reason ?? "Residual mapping could not resolve the child region.");
        results[childIntent.CandidateId].Set(
          DecisionCandidateStatus.Unknown,
          mapping.Reason ?? "Residual mapping could not resolve the child region.");
        relations.Add(new DecisionRelation(
          parentIntent.CandidateId,
          childIntent.CandidateId,
          DecisionRelationKind.Requires,
          mapping.Reason ?? "Residual mapping is Unknown.",
          parentIntent.ProofReferences.ToArray()));
        return;
      }
    }

    AddConflict(parent, child, results, relations, "Nested candidates have no valid dominance or residual composition proof.");
  }

  private static void AddConflict(
    DecisionUnit left,
    DecisionUnit right,
    IDictionary<string, MutableResult> results,
    ICollection<DecisionRelation> relations,
    string reason)
  {
    results[left.Intent.CandidateId].Set(DecisionCandidateStatus.Conflict, reason);
    results[right.Intent.CandidateId].Set(DecisionCandidateStatus.Conflict, reason);
    relations.Add(new DecisionRelation(
      left.Intent.CandidateId,
      right.Intent.CandidateId,
      DecisionRelationKind.Conflicts,
      reason));
  }

  private static bool IsAncestor(DecisionUnit ancestor, DecisionUnit descendant)
  {
    return ancestor.Intent.Anchor is not null &&
      descendant.Intent.Anchor.Ancestors().Any(node => ReferenceEquals(node, ancestor.Intent.Anchor));
  }

  private static void AddAtomicRelations(
    IEnumerable<DecisionUnit> group,
    ICollection<DecisionRelation> relations,
    string reason)
  {
    var members = group.OrderBy(candidate => candidate.Intent.CandidateId, StringComparer.Ordinal).ToArray();
    foreach (var member in members)
    {
      relations.Add(new DecisionRelation(
        member.Intent.AtomicGroup!,
        member.Intent.CandidateId,
        DecisionRelationKind.MemberOfAtomicGroup,
        reason));
    }
  }

  private static DecisionUnit CreateCompatibilityUnit(EditIntent intent)
  {
    var fragment = DecisionCpgFactory.CreateFragment(
      $"intent:{intent.CandidateId}",
      intent.Anchor,
      "anchor",
      intent.Action);
    var unitNode = DecisionCpgFactory.CreateUnit(intent.CandidateId, intent.Action, fragment, intent.CandidateId);
    return new DecisionUnit(
      intent.CandidateId,
      intent.Action,
      unitNode,
      new[] { fragment },
      new[] { DecisionCpgFactory.CreateContainment(unitNode, fragment) },
      DecisionCpgFactory.CreateSyntaxBindings((fragment, intent.Anchor)),
      reason: intent.CandidateId,
      intent: intent);
  }

  private static RuleDecision ToRuleDecision(DecisionUnit unit)
  {
    var anchor = unit.Intent.Anchor;
    var replacementFragment = unit.Fragments
      .FirstOrDefault(fragment => string.Equals(DecisionCpgFactory.GetFragmentRole(fragment), "replacement", StringComparison.Ordinal));
    var replacement = replacementFragment is { NodeId: { } nodeId } &&
      unit.SyntaxBindings.TryGetValue(nodeId, out var replacementNode)
      ? replacementNode
      : null;
    return new RuleDecision(
      anchor,
      anchor,
      unit.Action,
      unit.Reason,
      replacement,
      unit.RuleId,
      unit.Intent,
      unit.Footprint);
  }

  private sealed class MutableResult
  {
    private string _reason;

    public MutableResult(DecisionUnit candidate)
    {
      Candidate = candidate;
      _reason = "Candidate was not evaluated yet.";
    }

    public DecisionUnit Candidate { get; }

    public DecisionCandidateStatus? Status { get; private set; }

    public void Set(DecisionCandidateStatus status, string reason)
    {
      if (Status is DecisionCandidateStatus.Blocked && status != DecisionCandidateStatus.Blocked)
      {
        return;
      }

      Status = Status switch
      {
        null => status,
        DecisionCandidateStatus.Selected when status == DecisionCandidateStatus.Dominated => status,
        DecisionCandidateStatus.Selected when status == DecisionCandidateStatus.Composed => status,
        DecisionCandidateStatus.Composed when status == DecisionCandidateStatus.Dominated => status,
        DecisionCandidateStatus.Dominated when status == DecisionCandidateStatus.Selected => Status,
        _ when Status == status => Status,
        _ when status is DecisionCandidateStatus.Conflict or DecisionCandidateStatus.Blocked => status,
        _ => Status
      };
      _reason = reason;
    }

    public DecisionCandidateResult Build(IReadOnlyList<DecisionRelation> relations)
    {
      var resolvedStatus = Status ?? DecisionCandidateStatus.Unknown;
      return new DecisionCandidateResult(
        Candidate.Intent.CandidateId,
        resolvedStatus,
        Candidate,
        _reason,
        relations.Where(relation =>
          string.Equals(relation.FromCandidateId, Candidate.Intent.CandidateId, StringComparison.Ordinal) ||
          string.Equals(relation.ToCandidateId, Candidate.Intent.CandidateId, StringComparison.Ordinal)).ToArray());
    }
  }
}
