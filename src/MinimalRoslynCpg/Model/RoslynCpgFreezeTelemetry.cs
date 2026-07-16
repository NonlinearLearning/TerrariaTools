namespace MinimalRoslynCpg.Model;

public sealed record RoslynCpgFreezeTelemetry(
  long TotalElapsedMilliseconds,
  long AssignDeterministicNodeIdsElapsedMilliseconds,
  long CreateAnchorsElapsedMilliseconds,
  long CreateNodeIdTableElapsedMilliseconds,
  long RemapNodesElapsedMilliseconds,
  long RemapEdgesElapsedMilliseconds,
  long BuildQueryIndexElapsedMilliseconds,
  long PopulateEdgeIndexBucketsElapsedMilliseconds,
  long OrderEdgesElapsedMilliseconds,
  long OrderNodesElapsedMilliseconds,
  long SnapshotHashElapsedMilliseconds,
  long BuildAdjacencyElapsedMilliseconds,
  long BuildKindAdjacencyElapsedMilliseconds,
  long BuildEdgeKindIndexElapsedMilliseconds,
  long BuildNodeKindIndexElapsedMilliseconds,
  long BuildFilePathIndexElapsedMilliseconds,
  int NodeCount,
  int EdgeCount,
  int DistinctAnchorCount)
{
  public static RoslynCpgFreezeTelemetry CreateDefault()
  {
    return new RoslynCpgFreezeTelemetry(
      TotalElapsedMilliseconds: 0,
      AssignDeterministicNodeIdsElapsedMilliseconds: 0,
      CreateAnchorsElapsedMilliseconds: 0,
      CreateNodeIdTableElapsedMilliseconds: 0,
      RemapNodesElapsedMilliseconds: 0,
      RemapEdgesElapsedMilliseconds: 0,
      BuildQueryIndexElapsedMilliseconds: 0,
      PopulateEdgeIndexBucketsElapsedMilliseconds: 0,
      OrderEdgesElapsedMilliseconds: 0,
      OrderNodesElapsedMilliseconds: 0,
      SnapshotHashElapsedMilliseconds: 0,
      BuildAdjacencyElapsedMilliseconds: 0,
      BuildKindAdjacencyElapsedMilliseconds: 0,
      BuildEdgeKindIndexElapsedMilliseconds: 0,
      BuildNodeKindIndexElapsedMilliseconds: 0,
      BuildFilePathIndexElapsedMilliseconds: 0,
      NodeCount: 0,
      EdgeCount: 0,
      DistinctAnchorCount: 0);
  }
}
