using NLCPG.Persistence;

namespace NLCPG.Validation;

/// <summary>
/// Verifies the complete set of persisted-shard inputs before a build becomes visible.
/// </summary>
public sealed class CpgPersistedBuildValidator
{
  public CpgValidationReport Validate(IReadOnlyCollection<CpgBuildRoutingShardEntry> entries)
  {
    ArgumentNullException.ThrowIfNull(entries);

    var issues = new List<CpgValidationIssue>();
    var primaryNodes = new HashSet<uint>();
    foreach (var entry in entries)
    {
      ValidateEntry(entry, issues);
      if (entry.Shard.Role != CpgShardRole.Primary)
      {
        continue;
      }

      if (entry.Shard.Role == CpgShardRole.Primary)
      {
        foreach (var group in entry.Shard.Nodes.GroupBy(node => node.NodeId))
        {
          primaryNodes.Add(group.Key);
          if (group.Count() > 1)
          {
            issues.Add(CreateIssue(
              "CPG100",
              $"primary-node:{entry.Location.ShardId}:{group.Key}",
              "A primary shard contains the same NodeId more than once.",
              group.Key));
          }
        }
      }
    }

    ValidateBoundaryEdges(entries, primaryNodes, issues);
    return new CpgValidationReport(issues.OrderBy(issue => issue.StableKey, StringComparer.Ordinal).ToArray());
  }

  private static void ValidateEntry(CpgBuildRoutingShardEntry entry, ICollection<CpgValidationIssue> issues)
  {
    if (entry.Location.Status != CpgShardStatus.Complete)
    {
      issues.Add(CreateIssue(
        "CPG101",
        $"location:{entry.Location.ShardId}",
        "A routing entry references a shard that is not complete."));
    }

    var localIndexes = entry.Shard.Nodes.Select(node => node.LocalIndex).ToHashSet();
    if (localIndexes.Count != entry.Shard.Nodes.Count ||
        localIndexes.Any(index => index < 0 || index >= entry.Shard.Nodes.Count) ||
        entry.Shard.Edges.Any(edge => !localIndexes.Contains(edge.SourceLocalIndex) ||
          !localIndexes.Contains(edge.TargetLocalIndex)))
    {
      issues.Add(CreateIssue(
        "CPG102",
        $"local-index:{entry.Location.ShardId}",
        "A persisted primary shard has invalid local node or edge indexes."));
    }

    if (entry.Shard.Role == CpgShardRole.BoundaryAdjacency &&
        (entry.Shard.BoundaryAdjacency is null || entry.Shard.Nodes.Count != 0 || entry.Shard.Edges.Count != 0))
    {
      issues.Add(CreateIssue(
        "CPG103",
        $"boundary-shard:{entry.Location.ShardId}",
        "A boundary-adjacency shard has invalid adjacency metadata or local graph content."));
    }
  }

  private static void ValidateBoundaryEdges(
    IEnumerable<CpgBuildRoutingShardEntry> entries,
    IReadOnlySet<uint> primaryNodes,
    ICollection<CpgValidationIssue> issues)
  {
    foreach (var entry in entries.Where(entry => entry.Shard.Role == CpgShardRole.BoundaryAdjacency))
    {
      foreach (var edge in entry.Shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
      {
        if (!primaryNodes.Contains(edge.SourceNodeId) || !primaryNodes.Contains(edge.TargetNodeId))
        {
          issues.Add(CreateIssue(
            "CPG104",
            $"boundary-endpoint:{entry.Location.ShardId}:{edge.SourceNodeId}:{edge.Kind}:{edge.TargetNodeId}",
            "A boundary edge references a NodeId without a primary shard owner.",
            edge.SourceNodeId));
        }
      }
    }
  }

  private static CpgValidationIssue CreateIssue(string code, string key, string message, uint? nodeId = null)
  {
    return new CpgValidationIssue(
      code,
      CpgValidationSeverity.Error,
      $"{code}:{key}",
      message,
      nodeId is { } value ? new NLCPG.Model.NodeId(value) : null);
  }
}
