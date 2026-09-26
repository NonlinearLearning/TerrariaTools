using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Analysis;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Model;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Core.Decision;

public enum AnalysisEvidenceKind
{
  SeedMark,
  Propagation,
  Lift,
  Query,
  UsesSummary,
  Proposal,
  Decision,
  EmptyOutput,
  Unavailable,
  Truncated
}

public enum AnalysisEvidenceEdgeKind
{
  Supports,
  DerivedFrom,
  UsesGraphEdge,
  UsesSummary,
  RejectedBy,
  MergedInto,
  TruncatedBy
}

public enum AnalysisEvidenceState
{
  Available,
  Empty,
  Unavailable,
  Truncated,
  Rejected
}

/// <summary>
/// A source-only reference used by evidence. It intentionally does not retain Roslyn objects.
/// </summary>
public sealed record EvidenceAnchor(
  string FilePath,
  int Start,
  int Length,
  string SyntaxKind,
  string? GraphNodeId = null)
{
  public static EvidenceAnchor FromSyntaxNode(SyntaxNode syntaxNode, NLCPGNode? graphNode = null)
  {
    ArgumentNullException.ThrowIfNull(syntaxNode);
    return new EvidenceAnchor(
      syntaxNode.SyntaxTree.FilePath ?? string.Empty,
      syntaxNode.SpanStart,
      syntaxNode.Span.Length,
      syntaxNode.Kind().ToString(),
      graphNode?.NodeId.ToString());
  }

  internal string StableKey => string.Join(
    "|",
    FilePath,
    Start.ToString(System.Globalization.CultureInfo.InvariantCulture),
    Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
    SyntaxKind,
    GraphNodeId ?? string.Empty);
}

public sealed record AnalysisEvidenceNode(
  string Id,
  AnalysisEvidenceKind Kind,
  string? RuleId,
  EvidenceAnchor? Anchor,
  string? SummaryKey,
  AnalysisEvidenceState State,
  string Description);

public sealed record AnalysisEvidenceEdge(
  string SourceId,
  string TargetId,
  AnalysisEvidenceEdgeKind Kind);

public sealed record EvidenceBudget(
  int MaxNodes = 10_000,
  int MaxEdges = 30_000,
  int MaxDescriptionLength = 240,
  int MaxSerializedBytes = 4_000_000);

public sealed record EvidenceBudgetSummary(
  int NodeCount,
  int EdgeCount,
  int MaxNodes,
  int MaxEdges,
  bool WasTruncated,
  int SerializedByteCount = 0,
  int MaxSerializedBytes = 0);

public sealed record DecisionEvidence(string RootNodeId, EvidenceBudgetSummary Budget);

/// <summary>
/// Immutable, canonically ordered evidence projection for one analysis epoch.
/// </summary>
public sealed record AnalysisEvidenceGraph(
  IReadOnlyList<AnalysisEvidenceNode> Nodes,
  IReadOnlyList<AnalysisEvidenceEdge> Edges,
  EvidenceBudgetSummary Budget)
{
  public static AnalysisEvidenceGraph Empty { get; } = new(
    Array.Empty<AnalysisEvidenceNode>(),
    Array.Empty<AnalysisEvidenceEdge>(),
    new EvidenceBudgetSummary(0, 0, 0, 0, false));

  public static AnalysisEvidenceGraph Combine(
    IReadOnlyList<AnalysisEvidenceGraph> graphs,
    EvidenceBudget? budget = null)
  {
    ArgumentNullException.ThrowIfNull(graphs);
    var resolvedBudget = budget ?? new EvidenceBudget();
    var allNodes = graphs
      .SelectMany(graph => graph.Nodes)
      .DistinctBy(node => node.Id)
      .OrderBy(node => node.Kind == AnalysisEvidenceKind.Decision ? 0 : 1)
      .ThenBy(node => node.Id, StringComparer.Ordinal)
      .ToList();
    var needsTruncation = allNodes.Count > resolvedBudget.MaxNodes;
    var nodeCapacity = needsTruncation ? Math.Max(0, resolvedBudget.MaxNodes - 1) : resolvedBudget.MaxNodes;
    var nodes = allNodes.Take(nodeCapacity).ToList();
    if (needsTruncation && resolvedBudget.MaxNodes > 0)
    {
      nodes.Add(new AnalysisEvidenceNode(
        "evidence-directory-budget-truncated",
        AnalysisEvidenceKind.Truncated,
        null,
        null,
        "directory-budget",
        AnalysisEvidenceState.Truncated,
        "Directory evidence budget was truncated."));
    }

    var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
    var allEdges = graphs
      .SelectMany(graph => graph.Edges)
      .Where(edge => nodeIds.Contains(edge.SourceId) && nodeIds.Contains(edge.TargetId))
      .Distinct()
      .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
      .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
      .ThenBy(edge => edge.Kind)
      .ToList();
    var edges = allEdges.Take(resolvedBudget.MaxEdges).ToList();
    var wasTruncated = needsTruncation ||
      allEdges.Count > resolvedBudget.MaxEdges ||
      graphs.Any(graph => graph.Budget.WasTruncated);
    var byteTruncated = TrimToSerializedByteBudget(nodes, edges, resolvedBudget);
    wasTruncated |= byteTruncated;
    if (wasTruncated && nodes.All(node => node.Kind != AnalysisEvidenceKind.Truncated) && resolvedBudget.MaxNodes > 0)
    {
      if (nodes.Count == resolvedBudget.MaxNodes)
      {
        nodes.RemoveAt(nodes.Count - 1);
      }

      nodes.Add(CreateDirectoryTruncationNode());
      var retainedNodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
      edges.RemoveAll(edge => !retainedNodeIds.Contains(edge.SourceId) || !retainedNodeIds.Contains(edge.TargetId));
      TrimToSerializedByteBudget(nodes, edges, resolvedBudget);
    }

    var serializedByteCount = GetSerializedByteCount(nodes, edges, resolvedBudget, wasTruncated);
    return new AnalysisEvidenceGraph(
      nodes,
      edges,
      new EvidenceBudgetSummary(
        nodes.Count,
        edges.Count,
        resolvedBudget.MaxNodes,
        resolvedBudget.MaxEdges,
        wasTruncated,
        serializedByteCount,
        resolvedBudget.MaxSerializedBytes));
  }

  private static AnalysisEvidenceNode CreateDirectoryTruncationNode()
  {
    return new AnalysisEvidenceNode(
      "evidence-directory-budget-truncated",
      AnalysisEvidenceKind.Truncated,
      null,
      null,
      "directory-budget",
      AnalysisEvidenceState.Truncated,
      "Directory evidence budget was truncated.");
  }

  private static bool TrimToSerializedByteBudget(
    List<AnalysisEvidenceNode> nodes,
    List<AnalysisEvidenceEdge> edges,
    EvidenceBudget budget)
  {
    var trimmed = false;
    while (GetSerializedByteCount(nodes, edges, budget, true) > budget.MaxSerializedBytes && edges.Count > 0)
    {
      edges.RemoveAt(edges.Count - 1);
      trimmed = true;
    }

    while (GetSerializedByteCount(nodes, edges, budget, true) > budget.MaxSerializedBytes && nodes.Count > 0)
    {
      var nodeIndex = nodes.FindLastIndex(node => node.Kind != AnalysisEvidenceKind.Truncated);
      if (nodeIndex < 0)
      {
        break;
      }

      var removedNodeId = nodes[nodeIndex].Id;
      nodes.RemoveAt(nodeIndex);
      edges.RemoveAll(edge => edge.SourceId == removedNodeId || edge.TargetId == removedNodeId);
      trimmed = true;
    }

    return trimmed;
  }

  private static int GetSerializedByteCount(
    IReadOnlyList<AnalysisEvidenceNode> nodes,
    IReadOnlyList<AnalysisEvidenceEdge> edges,
    EvidenceBudget budget,
    bool wasTruncated)
  {
    var projection = new AnalysisEvidenceGraph(
      nodes,
      edges,
      new EvidenceBudgetSummary(
        nodes.Count,
        edges.Count,
        budget.MaxNodes,
        budget.MaxEdges,
        wasTruncated,
        0,
        budget.MaxSerializedBytes));
    return JsonSerializer.SerializeToUtf8Bytes(projection, EvidenceJsonOptions).Length;
  }

  private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };
}

/// <summary>
/// Collects stage-local facts concurrently and creates stable IDs during one final ordered commit.
/// </summary>
public sealed class AnalysisEvidenceCollector
{
  private readonly object _gate = new();
  private readonly EvidenceBudget _budget;
  private readonly List<PendingNode> _nodes = new();
  private readonly List<PendingEdge> _edges = new();

  public AnalysisEvidenceCollector(EvidenceBudget? budget = null)
  {
    _budget = budget ?? new EvidenceBudget();
  }

  public void RecordSeed(MarkRecord mark)
  {
    AddNode(CreateMarkNode(AnalysisEvidenceKind.SeedMark, mark.RuleId, mark));
  }

  public void RecordPropagation(
    string ruleId,
    IReadOnlyList<MarkRecord> inputMarks,
    IReadOnlyList<PropagatedMarkRecord> outputMarks)
  {
    foreach (PropagatedMarkRecord output in outputMarks)
    {
      PendingNode outputNode = CreateMarkNode(
        AnalysisEvidenceKind.Propagation,
        ruleId,
        output.Mark,
        summaryKey: BuildPropagationSummary(output));
      AddNode(outputNode);
      var sourceKind = output.Depth > 1
        ? AnalysisEvidenceKind.Propagation
        : AnalysisEvidenceKind.SeedMark;
      foreach (MarkRecord input in inputMarks)
      {
        AddEdge(CreateMarkKey(sourceKind, input.RuleId, input), outputNode.Key,
          AnalysisEvidenceEdgeKind.DerivedFrom);
      }
    }
  }

  public void RecordFlowSummary(SyntaxNode invocationSyntax, ResolvedCallFlow result)
  {
    var anchor = EvidenceAnchor.FromSyntaxNode(invocationSyntax);
    var summary = string.Join(";", result.Status, result.Resolution, result.MethodKey.StableKey,
      result.Mapping?.Source, result.Mapping?.Target);
    var state = result.Status == ResolvedCallFlowStatus.Resolved
      ? AnalysisEvidenceState.Available
      : result.Status == ResolvedCallFlowStatus.Blocked
        ? AnalysisEvidenceState.Rejected
        : AnalysisEvidenceState.Unavailable;
    AddNode(new PendingNode(
      CreateKey(AnalysisEvidenceKind.UsesSummary, null, anchor, summary),
      AnalysisEvidenceKind.UsesSummary,
      null,
      anchor,
      summary,
      state,
      BuildDescription(result.RejectionReason ?? "Flow summary resolved.")));
  }

  public void RecordLift(
    string ruleId,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
    IReadOnlyList<LiftedMarkRecord> outputMarks)
  {
    foreach (LiftedMarkRecord output in outputMarks)
    {
      PendingNode outputNode = CreateMarkNode(AnalysisEvidenceKind.Lift, ruleId, output.Mark);
      AddNode(outputNode);
      foreach (MarkRecord input in seedMarks)
      {
        AddEdge(CreateMarkKey(AnalysisEvidenceKind.SeedMark, input.RuleId, input), outputNode.Key,
          AnalysisEvidenceEdgeKind.DerivedFrom);
      }

      foreach (PropagatedMarkRecord input in propagatedMarks)
      {
        AddEdge(CreateMarkKey(AnalysisEvidenceKind.Propagation, input.RuleId, input.Mark), outputNode.Key,
          AnalysisEvidenceEdgeKind.DerivedFrom);
      }
    }
  }

  public void RecordProposal(
    string ruleId,
    IReadOnlyList<object> inputs,
    IReadOnlyList<DecisionUnit> units)
  {
    var inputKeys = inputs.Select(GetInputKey).Where(key => key is not null).Cast<string>().Distinct().ToList();
    foreach (DecisionUnit unit in units)
    {
      var anchor = TryGetAnchor(unit);
      var key = CreateKey(AnalysisEvidenceKind.Proposal, ruleId, anchor, unit.Action.ToString());
      AddNode(new PendingNode(
        key,
        AnalysisEvidenceKind.Proposal,
        ruleId,
        anchor,
        unit.Action.ToString(),
        AnalysisEvidenceState.Available,
        BuildDescription(unit.Reason)));
      foreach (string inputKey in inputKeys)
      {
        AddEdge(inputKey, key, AnalysisEvidenceEdgeKind.Supports);
      }
    }
  }

  public void RecordQuery(
    NodeId sinkNodeId,
    NLCPGSliceQueryOptions options,
    NLCPGSliceResult result,
    bool wasCacheHit)
  {
    var summary = string.Join(
      ";",
      sinkNodeId.ToString(),
      options.MaxHops.ToString(System.Globalization.CultureInfo.InvariantCulture),
      options.MaxPaths.ToString(System.Globalization.CultureInfo.InvariantCulture),
      options.MaxVisitedNodes.ToString(System.Globalization.CultureInfo.InvariantCulture),
      options.MaxVisitedEdges.ToString(System.Globalization.CultureInfo.InvariantCulture),
      options.MaxCachedStates.ToString(System.Globalization.CultureInfo.InvariantCulture),
      options.MaxCallerFanout.ToString(System.Globalization.CultureInfo.InvariantCulture),
      wasCacheHit.ToString(),
      result.VisitedNodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
      result.VisitedEdgeCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    var state = result.WasTruncated ? AnalysisEvidenceState.Truncated : AnalysisEvidenceState.Available;
    var queryKey = CreateKey(AnalysisEvidenceKind.Query, null, null, summary);
    AddNode(new PendingNode(
      queryKey,
      AnalysisEvidenceKind.Query,
      null,
      null,
      summary,
      state,
      BuildDescription(result.WasTruncated
        ? result.TruncationReason ?? "CPG query budget was truncated."
        : "CPG query completed.")));
    if (result.WasTruncated)
    {
      var truncationKey = CreateKey(AnalysisEvidenceKind.Truncated, null, null, summary);
      AddNode(new PendingNode(
        truncationKey,
        AnalysisEvidenceKind.Truncated,
        null,
        null,
        summary,
        AnalysisEvidenceState.Truncated,
        BuildDescription(result.TruncationReason ?? "CPG query budget was truncated.")));
      AddEdge(queryKey, truncationKey, AnalysisEvidenceEdgeKind.TruncatedBy);
    }
  }

  public void RecordRelationQuery(CpgRelationQuery query, CpgRelationQueryResult result)
  {
    var summary = string.Join(
      ";",
      query.Profile,
      query.Direction,
      query.Purpose,
      query.Budget.MaxHops.ToString(System.Globalization.CultureInfo.InvariantCulture),
      query.Budget.MaxPaths.ToString(System.Globalization.CultureInfo.InvariantCulture),
      query.Budget.MaxVisitedNodes.ToString(System.Globalization.CultureInfo.InvariantCulture),
      query.Budget.MaxVisitedEdges.ToString(System.Globalization.CultureInfo.InvariantCulture),
      result.Status,
      result.WasCacheHit);
    var state = result.Status == CpgQueryStatus.Truncated
      ? AnalysisEvidenceState.Truncated
      : result.Status == CpgQueryStatus.Unavailable
        ? AnalysisEvidenceState.Unavailable
        : AnalysisEvidenceState.Available;
    AddNode(new PendingNode(
      CreateKey(AnalysisEvidenceKind.Query, null, null, summary),
      AnalysisEvidenceKind.Query,
      null,
      null,
      summary,
      state,
      BuildDescription(result.TruncationReason ?? result.Status.ToString())));
    if (result.Status == CpgQueryStatus.Truncated)
    {
      var queryKey = CreateKey(AnalysisEvidenceKind.Query, null, null, summary);
      var truncationKey = CreateKey(AnalysisEvidenceKind.Truncated, null, null, summary);
      AddNode(new PendingNode(
        truncationKey,
        AnalysisEvidenceKind.Truncated,
        null,
        null,
        summary,
        AnalysisEvidenceState.Truncated,
        BuildDescription(result.TruncationReason ?? "CPG relation query budget was truncated.")));
      AddEdge(queryKey, truncationKey, AnalysisEvidenceEdgeKind.TruncatedBy);
    }
  }

  public void RecordNodeStatuses(IReadOnlyDictionary<RuleNodeId, RuleGraphNodeStatus> statuses)
  {
    foreach ((RuleNodeId nodeId, RuleGraphNodeStatus status) in statuses)
    {
      if (status == RuleGraphNodeStatus.Completed)
      {
        continue;
      }

      AddNode(new PendingNode(
        CreateKey(AnalysisEvidenceKind.Unavailable, nodeId.Value, null, status.ToString()),
        AnalysisEvidenceKind.Unavailable,
        nodeId.Value,
        null,
        status.ToString(),
        AnalysisEvidenceState.Unavailable,
        BuildDescription($"Rule graph node status: {status}.")));
    }
  }

  public void RecordEmptyOutputs(IReadOnlyList<RuleGraphExecutionNodeResult> nodes)
  {
    foreach (RuleGraphExecutionNodeResult node in nodes)
    {
      if (node.Status != RuleGraphNodeStatus.Completed || node.Result.Values.Count != 0)
      {
        continue;
      }

      AddNode(new PendingNode(
        CreateKey(AnalysisEvidenceKind.EmptyOutput, node.NodeId.Value, null, "empty-output"),
        AnalysisEvidenceKind.EmptyOutput,
        node.NodeId.Value,
        null,
        "empty-output",
        AnalysisEvidenceState.Empty,
        "Rule graph node completed without output."));
    }
  }

  public EvidenceCollectionResult Complete(IReadOnlyList<RuleDecision> decisions)
  {
    var decisionKeys = new Dictionary<RuleDecision, string>();
    foreach (RuleDecision decision in decisions)
    {
      EvidenceAnchor anchor = EvidenceAnchor.FromSyntaxNode(decision.FinalNode);
      var key = CreateKey(AnalysisEvidenceKind.Decision, null, anchor, decision.Action.ToString());
      decisionKeys[decision] = key;
      AddNode(new PendingNode(
        key,
        AnalysisEvidenceKind.Decision,
        null,
        anchor,
        decision.Action.ToString(),
        AnalysisEvidenceState.Available,
        BuildDescription($"{decision.Action} decision.")));
      AddDecisionSupportEdges(key, anchor, decision);
    }

    var graph = BuildGraph();
    var decisionsWithEvidence = decisions
      .Select(decision => decision with
      {
        EvidenceRootId = GetId(decisionKeys[decision]),
        Evidence = new DecisionEvidence(GetId(decisionKeys[decision]), graph.Budget)
      })
      .ToList();
    return new EvidenceCollectionResult(graph, decisionsWithEvidence);
  }

  private void AddDecisionSupportEdges(
    string decisionKey,
    EvidenceAnchor anchor,
    RuleDecision decision)
  {
    List<PendingNode> candidates;
    lock (_gate)
    {
      candidates = _nodes
        .Where(node => node.Kind == AnalysisEvidenceKind.Proposal && node.Anchor is not null)
        .ToList();
    }

    var exactCandidates = candidates
      .Where(candidate => IsSameSyntaxAnchor(candidate.Anchor!, anchor))
      .ToList();
    var winner = exactCandidates
      .Where(candidate => candidate.SummaryKey == decision.Action.ToString())
      .OrderByDescending(candidate => candidate.Description == BuildDescription(decision.Reason))
      .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
      .FirstOrDefault();
    foreach (PendingNode candidate in candidates)
    {
      if (IsSameSyntaxAnchor(candidate.Anchor!, anchor))
      {
        AddEdge(candidate.Key, decisionKey, candidate == winner
          ? AnalysisEvidenceEdgeKind.Supports
          : AnalysisEvidenceEdgeKind.RejectedBy);
        continue;
      }

      if (candidate.Anchor is { } candidateAnchor && IsCoveredBy(anchor, candidateAnchor))
      {
        AddEdge(candidate.Key, decisionKey, AnalysisEvidenceEdgeKind.MergedInto);
      }
    }
  }

  private static bool IsCoveredBy(EvidenceAnchor parent, EvidenceAnchor child)
  {
    return string.Equals(parent.FilePath, child.FilePath, StringComparison.Ordinal) &&
      parent.Start <= child.Start &&
      parent.Start + parent.Length >= child.Start + child.Length;
  }

  private static bool IsSameSyntaxAnchor(EvidenceAnchor left, EvidenceAnchor right)
  {
    return string.Equals(left.FilePath, right.FilePath, StringComparison.Ordinal) &&
      left.Start == right.Start &&
      left.Length == right.Length &&
      string.Equals(left.SyntaxKind, right.SyntaxKind, StringComparison.Ordinal);
  }

  private AnalysisEvidenceGraph BuildGraph()
  {
    List<PendingNode> pendingNodes;
    List<PendingEdge> pendingEdges;
    lock (_gate)
    {
      pendingNodes = _nodes.ToList();
      pendingEdges = _edges.ToList();
    }

    var distinctNodes = pendingNodes
      .GroupBy(node => node.Key, StringComparer.Ordinal)
      .Select(group => group.OrderBy(node => node.SortKey, StringComparer.Ordinal).First())
      .OrderBy(node => node.Kind == AnalysisEvidenceKind.Decision ? 0 : 1)
      .ThenBy(node => node.SortKey, StringComparer.Ordinal)
      .ToList();
    var decisionRootCount = distinctNodes.Count(node => node.Kind == AnalysisEvidenceKind.Decision);
    var nodeCapacity = Math.Max(_budget.MaxNodes, decisionRootCount);
    var canonicalNodes = distinctNodes
      .Take(nodeCapacity)
      .ToList();
    var acceptedKeys = canonicalNodes.Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
    var canonicalEdges = pendingEdges
      .Where(edge => acceptedKeys.Contains(edge.SourceKey) && acceptedKeys.Contains(edge.TargetKey))
      .Distinct()
      .OrderBy(edge => edge.SourceKey, StringComparer.Ordinal)
      .ThenBy(edge => edge.TargetKey, StringComparer.Ordinal)
      .ThenBy(edge => edge.Kind)
      .Take(_budget.MaxEdges)
      .ToList();
    var wasTruncated = pendingNodes.Count > canonicalNodes.Count || pendingEdges.Count > canonicalEdges.Count;
    if (wasTruncated && canonicalNodes.Count < _budget.MaxNodes)
    {
      var truncation = new PendingNode(
        CreateKey(AnalysisEvidenceKind.Truncated, null, null, "evidence-budget"),
        AnalysisEvidenceKind.Truncated,
        null,
        null,
        "evidence-budget",
        AnalysisEvidenceState.Truncated,
        "Evidence budget was truncated.");
      canonicalNodes.Add(truncation);
      acceptedKeys.Add(truncation.Key);
    }

    var nodeIdMap = canonicalNodes.ToDictionary(node => node.Key, node => GetId(node.Key), StringComparer.Ordinal);
    var nodes = canonicalNodes.Select(node => new AnalysisEvidenceNode(
      nodeIdMap[node.Key],
      node.Kind,
      node.RuleId,
      node.Anchor,
      node.SummaryKey,
      node.State,
      node.Description)).ToList();
    var edges = canonicalEdges.Select(edge => new AnalysisEvidenceEdge(
      nodeIdMap[edge.SourceKey],
      nodeIdMap[edge.TargetKey],
      edge.Kind)).ToList();
    return new AnalysisEvidenceGraph(
      nodes,
      edges,
      new EvidenceBudgetSummary(nodes.Count, edges.Count, _budget.MaxNodes, _budget.MaxEdges, wasTruncated));
  }

  private PendingNode CreateMarkNode(
    AnalysisEvidenceKind kind,
    string ruleId,
    MarkRecord mark,
    string? summaryKey = null)
  {
    var anchor = EvidenceAnchor.FromSyntaxNode(mark.SyntaxNode, mark.PrimaryGraphNode);
    var nodeKey = kind == AnalysisEvidenceKind.Propagation
      ? CreateKey(kind, ruleId, anchor, null)
      : CreateKey(kind, ruleId, anchor, summaryKey);
    return new PendingNode(
      nodeKey,
      kind,
      ruleId,
      anchor,
      summaryKey,
      AnalysisEvidenceState.Available,
      BuildDescription(mark.Reason));
  }

  private static string CreateMarkKey(AnalysisEvidenceKind kind, string ruleId, MarkRecord mark)
  {
    return CreateKey(kind, ruleId, EvidenceAnchor.FromSyntaxNode(mark.SyntaxNode, mark.PrimaryGraphNode), null);
  }

  private static string? GetInputKey(object input)
  {
    return input switch
    {
      MarkRecord mark => CreateMarkKey(AnalysisEvidenceKind.SeedMark, mark.RuleId, mark),
      PropagatedMarkRecord mark => CreateMarkKey(AnalysisEvidenceKind.Propagation, mark.RuleId, mark.Mark),
      LiftedMarkRecord mark => CreateMarkKey(AnalysisEvidenceKind.Lift, mark.RuleId, mark.Mark),
      _ => null
    };
  }

  private static EvidenceAnchor? TryGetAnchor(DecisionUnit unit)
  {
    if (unit.Fragments.Count == 0 || unit.Fragments[0].NodeId is not { } nodeId ||
        !unit.SyntaxBindings.TryGetValue(nodeId, out SyntaxNode? syntaxNode))
    {
      return null;
    }

    return EvidenceAnchor.FromSyntaxNode(syntaxNode, unit.Fragments[0]);
  }

  private void AddNode(PendingNode node)
  {
    lock (_gate)
    {
      _nodes.Add(node);
    }
  }

  private void AddEdge(string sourceKey, string targetKey, AnalysisEvidenceEdgeKind kind)
  {
    lock (_gate)
    {
      _edges.Add(new PendingEdge(sourceKey, targetKey, kind));
    }
  }

  private static string CreateKey(
    AnalysisEvidenceKind kind,
    string? ruleId,
    EvidenceAnchor? anchor,
    string? summary)
  {
    return string.Join("|", kind, ruleId ?? string.Empty, anchor?.StableKey ?? string.Empty, summary ?? string.Empty);
  }

  private static string GetId(string key)
  {
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    return "evidence-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
  }

  private static string BuildPayloadSummary(object? payload)
  {
    return payload is null ? string.Empty : payload.GetType().FullName ?? payload.GetType().Name;
  }

  private static string BuildPropagationSummary(PropagatedMarkRecord output)
  {
    var payloadSummary = BuildPayloadSummary(output.Payload);
    return payloadSummary.Length == 0
      ? $"depth:{output.Depth}"
      : $"depth:{output.Depth};payload:{payloadSummary}";
  }

  private string BuildDescription(string description)
  {
    if (string.IsNullOrWhiteSpace(description))
    {
      return string.Empty;
    }

    return description.Length <= _budget.MaxDescriptionLength
      ? description
      : description[.._budget.MaxDescriptionLength];
  }

  private readonly record struct PendingNode(
    string Key,
    AnalysisEvidenceKind Kind,
    string? RuleId,
    EvidenceAnchor? Anchor,
    string? SummaryKey,
    AnalysisEvidenceState State,
    string Description)
  {
    public string SortKey => Key;
  }

  private readonly record struct PendingEdge(
    string SourceKey,
    string TargetKey,
    AnalysisEvidenceEdgeKind Kind);
}

public sealed record EvidenceCollectionResult(
  AnalysisEvidenceGraph Graph,
  IReadOnlyList<RuleDecision> Decisions);
