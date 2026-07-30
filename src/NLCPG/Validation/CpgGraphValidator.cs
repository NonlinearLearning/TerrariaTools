using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Validation;

public enum CpgValidationSeverity
{
  Error,
  Warning,
  Info,
}

public sealed record CpgValidationIssue(
  string Code,
  CpgValidationSeverity Severity,
  string StableKey,
  string Message,
  NodeId? NodeId = null);

public sealed record CpgValidationReport(IReadOnlyList<CpgValidationIssue> Issues)
{
  public bool IsValid => Issues.All(issue => issue.Severity != CpgValidationSeverity.Error);

  public int ErrorCount => Issues.Count(issue => issue.Severity == CpgValidationSeverity.Error);
}

/// <summary>
/// Checks immutable CPG facts without mutating the graph or rebuilding passes.
/// </summary>
public sealed class CpgGraphValidator
{
  public CpgValidationReport Validate(
    NLCPGGraph graph,
    NLCPGCapability requestedCapabilities = NLCPGCapability.None,
    NLCPGCapability builtCapabilities = NLCPGCapability.None)
  {
    ArgumentNullException.ThrowIfNull(graph);

    var issues = new List<CpgValidationIssue>();
    if (!graph.HasQueryIndex)
    {
      issues.Add(CreateIssue(
        "CPG000",
        CpgValidationSeverity.Error,
        "graph",
        "The graph must be frozen before validation."));
    }

    issues.AddRange(ValidateFrozenFacts(
      graph.Nodes.ToArray(),
      graph.Edges.ToArray(),
      requestedCapabilities,
      builtCapabilities).Issues);

    if (graph.HasQueryIndex)
    {
      ValidateQueryIndexes(graph, issues);
    }

    return CreateReport(issues);
  }

  /// <summary>
  /// Validates a candidate frozen snapshot. Tests and persistence boundaries can use this
  /// overload to diagnose malformed data before creating an NLCPGGraph instance.
  /// </summary>
  public CpgValidationReport ValidateFrozenFacts(
    IReadOnlyList<NLCPGNode> nodes,
    IReadOnlyList<NLCPGEdge> edges,
    NLCPGCapability requestedCapabilities = NLCPGCapability.None,
    NLCPGCapability builtCapabilities = NLCPGCapability.None)
  {
    ArgumentNullException.ThrowIfNull(nodes);
    ArgumentNullException.ThrowIfNull(edges);

    var issues = new List<CpgValidationIssue>();
    var nodeIds = new HashSet<NodeId>();
    foreach (var node in nodes)
    {
      if (!node.NodeId.HasValue)
      {
        issues.Add(CreateIssue(
          "CPG001",
          CpgValidationSeverity.Error,
          $"node:{node.DisplayKind}",
          "Frozen graph nodes require a NodeId."));
        continue;
      }

      if (!nodeIds.Add(node.NodeId.Value))
      {
        issues.Add(CreateIssue(
          "CPG002",
          CpgValidationSeverity.Error,
          $"node:{node.NodeId.Value}",
          "NodeId is duplicated in the frozen graph.",
          node.NodeId));
      }
    }

    foreach (var edge in edges)
    {
      if (!nodeIds.Contains(edge.SourceNodeId) || !nodeIds.Contains(edge.TargetNodeId))
      {
        issues.Add(CreateIssue(
          "CPG003",
          CpgValidationSeverity.Error,
          $"edge:{edge.SourceNodeId}:{edge.Kind}:{edge.TargetNodeId}",
          "An edge references a node that is absent from the frozen graph.",
          edge.SourceNodeId));
      }
    }

    foreach (var group in nodes
      .Where(node => node.StableAnchor.HasValue)
      .GroupBy(node => node.StableAnchor!.Value))
    {
      if (group.Count() > 1)
      {
        issues.Add(CreateIssue(
          "CPG004",
          CpgValidationSeverity.Error,
          $"anchor:{group.Key}",
          "A stable node anchor is assigned to more than one frozen node.",
          group.First().NodeId));
      }
    }

    foreach (var group in nodes
      .Where(node => node.StableAnchor.HasValue)
      .GroupBy(node => new
      {
        node.StableAnchor!.Value.FilePathId,
        node.StableAnchor.Value.SpanStart,
        node.StableAnchor.Value.SpanEnd,
        node.StableAnchor.Value.Role,
        node.StableAnchor.Value.Ordinal,
      }))
    {
      if (group.Count() > 1)
      {
        issues.Add(CreateIssue(
          "CPG005",
          CpgValidationSeverity.Error,
          $"anchor-role:{group.Key.FilePathId}:{group.Key.SpanStart}:{group.Key.SpanEnd}:{group.Key.Role}:{group.Key.Ordinal}",
          "Stable node role and ordinal are duplicated at the same source anchor.",
          group.First().NodeId));
      }
    }

    foreach (var node in nodes.Where(node => node.StableAnchor is { } anchor && anchor.FilePathId != 0))
    {
      var anchor = node.StableAnchor!.Value;
      if (anchor.SpanStart < 0 || anchor.SpanEnd < anchor.SpanStart)
      {
        issues.Add(CreateIssue(
          "CPG006",
          CpgValidationSeverity.Error,
          $"anchor-span:{node.NodeId}",
          "A source-backed stable node anchor has an invalid span.",
          node.NodeId));
      }
    }

    ValidateCapabilities(requestedCapabilities, builtCapabilities, issues);
    return CreateReport(issues);
  }

  private static void ValidateQueryIndexes(NLCPGGraph graph, ICollection<CpgValidationIssue> issues)
  {
    foreach (var node in graph.Nodes.Where(node => node.NodeId.HasValue))
    {
      var nodeId = node.NodeId!.Value;
      ValidateEdgeOrdering(graph.GetOutgoingEdges(nodeId), "out", nodeId, issues);
      ValidateEdgeOrdering(graph.GetIncomingEdges(nodeId), "in", nodeId, issues);
    }

    foreach (var kind in Enum.GetValues<NLCPGEdgeKind>())
    {
      var indexed = graph.GetEdges(kind);
      var expected = graph.Edges.Where(edge => edge.Kind == kind).OrderBy(edge => edge.SourceNodeId)
        .ThenBy(edge => edge.Kind).ThenBy(edge => edge.TargetNodeId).ToArray();
      if (!indexed.SequenceEqual(expected))
      {
        issues.Add(CreateIssue(
          "CPG007",
          CpgValidationSeverity.Error,
          $"edge-kind:{kind}",
          "The edge-kind index differs from the frozen edge set."));
      }
    }

    foreach (var kind in Enum.GetValues<NLCPGNodeKind>())
    {
      var indexed = graph.GetNodes(kind);
      var expected = graph.Nodes.Where(node => node.Kind == kind).OrderBy(node => node.NodeId).ToArray();
      if (!indexed.SequenceEqual(expected))
      {
        issues.Add(CreateIssue(
          "CPG008",
          CpgValidationSeverity.Error,
          $"node-kind:{kind}",
          "The node-kind index differs from the frozen node set."));
      }
    }

    foreach (var group in graph.Nodes
      .Where(node => !string.IsNullOrWhiteSpace(node.FilePath) &&
        node.SpanStart.HasValue && node.SpanEnd.HasValue)
      .GroupBy(node => node.FilePath!, StringComparer.Ordinal))
    {
      var start = group.Min(node => node.SpanStart!.Value);
      var end = group.Max(node => node.SpanEnd!.Value);
      var indexed = graph.GetNodesInFileSpan(group.Key, start, end);
      var expected = group.OrderBy(node => node.SpanStart).ThenBy(node => node.SpanEnd)
        .ThenBy(node => node.NodeId).ToArray();
      if (!indexed.SequenceEqual(expected))
      {
        issues.Add(CreateIssue(
          "CPG011",
          CpgValidationSeverity.Error,
          $"file-span:{group.Key}:{start}:{end}",
          "The file-span index differs from the frozen node set."));
      }
    }
  }

  private static void ValidateEdgeOrdering(
    IReadOnlyList<NLCPGEdge> edges,
    string direction,
    NodeId nodeId,
    ICollection<CpgValidationIssue> issues)
  {
    var ordered = edges.OrderBy(edge => edge.SourceNodeId).ThenBy(edge => edge.Kind)
      .ThenBy(edge => edge.TargetNodeId).ToArray();
    if (!edges.SequenceEqual(ordered))
    {
      issues.Add(CreateIssue(
        "CPG009",
        CpgValidationSeverity.Error,
        $"adjacency:{direction}:{nodeId}",
        "An adjacency index is not in stable edge order.",
        nodeId));
    }
  }

  private static void ValidateCapabilities(
    NLCPGCapability requested,
    NLCPGCapability built,
    ICollection<CpgValidationIssue> issues)
  {
    if (requested == NLCPGCapability.None && built == NLCPGCapability.None)
    {
      return;
    }

    var required = ResolveClosure(requested);
    if ((built & required) != required)
    {
      var missing = required & ~built;
      issues.Add(CreateIssue(
        "CPG010",
        CpgValidationSeverity.Error,
        $"capability:{missing}",
        $"Requested CPG capabilities are not present in the built capability set: {missing}."));
    }
  }

  private static NLCPGCapability ResolveClosure(NLCPGCapability capabilities)
  {
    var resolved = capabilities;
    if ((resolved & (NLCPGCapability.MethodModel | NLCPGCapability.CallTargets |
                     NLCPGCapability.Cfg | NLCPGCapability.DataFlow |
                     NLCPGCapability.InterproceduralDataFlow | NLCPGCapability.Dominance |
                     NLCPGCapability.ControlDependence)) != 0)
    {
      resolved |= NLCPGCapability.MethodModel;
    }

    if ((resolved & NLCPGCapability.DataFlow) != 0)
    {
      resolved |= NLCPGCapability.CallTargets | NLCPGCapability.Cfg;
    }

    if ((resolved & NLCPGCapability.InterproceduralDataFlow) != 0)
    {
      resolved |= NLCPGCapability.DataFlow | NLCPGCapability.CallTargets |
                  NLCPGCapability.QueryIndex;
    }

    if ((resolved & NLCPGCapability.Dominance) != 0)
    {
      resolved |= NLCPGCapability.Cfg;
    }

    if ((resolved & NLCPGCapability.ControlDependence) != 0)
    {
      resolved |= NLCPGCapability.Dominance | NLCPGCapability.Cfg;
    }

    return resolved == NLCPGCapability.None
      ? NLCPGCapability.None
      : resolved | NLCPGCapability.SyntaxSemantic;
  }

  private static CpgValidationIssue CreateIssue(
    string code,
    CpgValidationSeverity severity,
    string key,
    string message,
    NodeId? nodeId = null)
  {
    return new CpgValidationIssue(code, severity, $"{code}:{key}", message, nodeId);
  }

  private static CpgValidationReport CreateReport(IEnumerable<CpgValidationIssue> issues)
  {
    return new CpgValidationReport(issues.OrderBy(issue => issue.StableKey, StringComparer.Ordinal).ToArray());
  }
}
