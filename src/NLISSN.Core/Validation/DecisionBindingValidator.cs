using Microsoft.CodeAnalysis;
using NLCPG.Model;
using NLISSN.Core.Decision;

namespace NLISSN.Core.Validation;

/// <summary>
/// Checks decision fragments and final rewrite decisions before source mutation.
/// </summary>
public sealed class DecisionBindingValidator
{
  public AnalysisValidationReport Validate(
    SyntaxNode compilationRoot,
    IReadOnlyList<DecisionUnit> units,
    IReadOnlyList<RuleDecision> decisions,
    AnalysisEvidenceGraph evidence)
  {
    ArgumentNullException.ThrowIfNull(compilationRoot);
    ArgumentNullException.ThrowIfNull(units);
    ArgumentNullException.ThrowIfNull(decisions);
    ArgumentNullException.ThrowIfNull(evidence);

    var issues = new List<ValidationIssue>();
    foreach (var unit in units)
    {
      ValidateUnit(compilationRoot, unit, issues);
    }

    foreach (var decision in decisions)
    {
      ValidateDecision(compilationRoot, decision, evidence, issues);
    }

    return AnalysisValidationReport.Create(issues);
  }

  private static void ValidateUnit(
    SyntaxNode compilationRoot,
    DecisionUnit unit,
    ICollection<ValidationIssue> issues)
  {
    if (unit.Fragments.Count == 0)
    {
      issues.Add(CreateIssue("DEC001", unit.RuleId, "A decision unit has no fragments.", unit.RuleId));
      return;
    }

    var fragmentIds = new HashSet<NodeId>();
    foreach (var fragment in unit.Fragments)
    {
      if (fragment.NodeId is not { } nodeId || !fragmentIds.Add(nodeId))
      {
        issues.Add(CreateIssue("DEC002", $"{unit.RuleId}:{fragment.Name}", "A decision fragment has no unique NodeId.", unit.RuleId));
        continue;
      }

      if (!unit.SyntaxBindings.TryGetValue(nodeId, out var syntaxNode))
      {
        issues.Add(CreateIssue("DEC003", $"{unit.RuleId}:{nodeId}", "A decision fragment has no syntax binding.", unit.RuleId, nodeId));
        continue;
      }

      if (!IsInOriginalTree(compilationRoot, syntaxNode) ||
          !string.Equals(fragment.FilePath, syntaxNode.SyntaxTree.FilePath, StringComparison.Ordinal) ||
          fragment.SpanStart != syntaxNode.SpanStart || fragment.SpanEnd != syntaxNode.Span.End)
      {
        issues.Add(CreateIssue("DEC004", $"{unit.RuleId}:{nodeId}", "A decision fragment binding is outside the original syntax tree or span.", unit.RuleId, nodeId));
      }
    }

    var replacementCount = unit.Fragments.Count(fragment => string.Equals(fragment.Name, "replacement", StringComparison.Ordinal));
    if (unit.Action == DecisionActionKind.Replace && replacementCount != 1)
    {
      issues.Add(CreateIssue("DEC005", unit.RuleId, "A Replace decision unit requires exactly one replacement fragment.", unit.RuleId));
    }

    if ((unit.Action is DecisionActionKind.Delete or DecisionActionKind.Skip) && replacementCount != 0)
    {
      issues.Add(CreateIssue("DEC006", unit.RuleId, "Delete and Skip decision units cannot contain replacement fragments.", unit.RuleId));
    }

    foreach (var relation in unit.Relations)
    {
      var isContainment = relation.Kind == NLCPG.Contracts.NLCPGEdgeKind.DecisionContains &&
        unit.UnitNode.NodeId is { } unitNodeId &&
        relation.SourceNodeId == unitNodeId &&
        fragmentIds.Contains(relation.TargetNodeId);
      var isFragmentRelation = relation.Kind == NLCPG.Contracts.NLCPGEdgeKind.DecisionRelation &&
        fragmentIds.Contains(relation.SourceNodeId) &&
        fragmentIds.Contains(relation.TargetNodeId);
      if (!isContainment && !isFragmentRelation)
      {
        issues.Add(CreateIssue("DEC007", $"{unit.RuleId}:{relation.SourceNodeId}:{relation.TargetNodeId}", "A decision relation references a fragment outside its decision unit.", unit.RuleId));
      }
    }
  }

  private static void ValidateDecision(
    SyntaxNode compilationRoot,
    RuleDecision decision,
    AnalysisEvidenceGraph evidence,
    ICollection<ValidationIssue> issues)
  {
    if (!IsInOriginalTree(compilationRoot, decision.OriginalNode) ||
        !IsInOriginalTree(compilationRoot, decision.FinalNode))
    {
      issues.Add(CreateIssue("DEC008", $"{decision.FinalNode.SpanStart}", "A final decision is bound to a different syntax tree.", null));
    }

    if (decision.Action == DecisionActionKind.Replace && decision.ReplacementNode is null)
    {
      issues.Add(CreateIssue("DEC009", $"{decision.FinalNode.SpanStart}", "A Replace decision requires a replacement binding.", null));
    }

    if (decision.Action is DecisionActionKind.Delete or DecisionActionKind.Skip && decision.ReplacementNode is not null)
    {
      issues.Add(CreateIssue("DEC010", $"{decision.FinalNode.SpanStart}", "Delete and Skip decisions cannot carry replacement bindings.", null));
    }

    if (string.IsNullOrWhiteSpace(decision.EvidenceRootId) ||
        !evidence.Nodes.Any(node =>
          string.Equals(node.Id, decision.EvidenceRootId, StringComparison.Ordinal) &&
          node.Kind == AnalysisEvidenceKind.Decision))
    {
      issues.Add(CreateIssue("DEC011", $"{decision.FinalNode.SpanStart}", "A final decision has no evidence root in the current analysis epoch.", null));
      return;
    }

    if (!HasInputFactPath(decision.EvidenceRootId, evidence))
    {
      issues.Add(CreateIssue("DEC012", $"{decision.FinalNode.SpanStart}", "A final decision evidence root has no path to an input fact.", null));
    }
  }

  private static bool HasInputFactPath(string rootId, AnalysisEvidenceGraph evidence)
  {
    var nodesById = evidence.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
    var parents = evidence.Edges
      .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).ToArray(), StringComparer.Ordinal);
    var pending = new Queue<string>();
    var visited = new HashSet<string>(StringComparer.Ordinal);
    pending.Enqueue(rootId);
    while (pending.Count > 0)
    {
      var current = pending.Dequeue();
      if (!visited.Add(current) || !nodesById.TryGetValue(current, out var node))
      {
        continue;
      }

      if (node.Kind == AnalysisEvidenceKind.SeedMark)
      {
        return true;
      }

      if (parents.TryGetValue(current, out var parentIds))
      {
        foreach (var parentId in parentIds)
        {
          pending.Enqueue(parentId);
        }
      }
    }

    return false;
  }

  private static bool IsInOriginalTree(SyntaxNode root, SyntaxNode node)
  {
    return ReferenceEquals(root.SyntaxTree, node.SyntaxTree) &&
      (ReferenceEquals(root, node) || node.AncestorsAndSelf().Any(candidate => ReferenceEquals(candidate, root)));
  }

  private static ValidationIssue CreateIssue(
    string code,
    string key,
    string message,
    string? ruleId,
    NodeId? nodeId = null)
  {
    return new ValidationIssue(code, ValidationSeverity.Error, $"{code}:{key}", message, ruleId, nodeId);
  }
}
