using NLCPG.Contracts;
using NLCPG.Model;
using NLCPG.Persistence;
using NLCPG.Validation;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgGraphValidatorTests
{
  [Fact]
  public void ValidateFrozenFacts_WhenSnapshotHasDuplicateIdsAndDanglingEdge_ReportsStableCodes()
  {
    var nodes = new[]
    {
      Node(new NodeId(1), new StableNodeAnchor(NLCPGNodeKind.SyntaxNode, 1, 0, 1, StableNodeRole.SyntaxNode, 0, 1)),
      Node(new NodeId(1), new StableNodeAnchor(NLCPGNodeKind.Operation, 1, 0, 1, StableNodeRole.Operation, 0, 2)),
    };
    var edges = new[] { new NLCPGEdge(new NodeId(1), new NodeId(9), NLCPGEdgeKind.OpHasSyntax) };

    var report = new CpgGraphValidator().ValidateFrozenFacts(nodes, edges);

    Assert.Equal(new[] { "CPG002", "CPG003" }, report.Issues.Select(issue => issue.Code));
  }

  [Fact]
  public void ValidateFrozenFacts_WhenRoleOrdinalCollides_ReportsStableCode()
  {
    var anchor = new StableNodeAnchor(
      NLCPGNodeKind.SyntaxNode,
      1,
      0,
      1,
      StableNodeRole.SyntaxNode,
      0,
      1);
    var conflictingAnchor = anchor with { ExtraKeyId = 2 };

    var report = new CpgGraphValidator().ValidateFrozenFacts(
      new[] { Node(new NodeId(1), anchor), Node(new NodeId(2), conflictingAnchor) },
      Array.Empty<NLCPGEdge>());

    Assert.Contains(report.Issues, issue => issue.Code == "CPG005");
  }

  [Fact]
  public void ValidateFrozenFacts_WhenCapabilityClosureIsMissing_ReportsStableCode()
  {
    var report = new CpgGraphValidator().ValidateFrozenFacts(
      Array.Empty<NLCPGNode>(),
      Array.Empty<NLCPGEdge>(),
      NLCPGCapability.ControlDependence,
      NLCPGCapability.ControlDependence);

    var issue = Assert.Single(report.Issues);
    Assert.Equal("CPG010", issue.Code);
    Assert.Equal(CpgValidationSeverity.Error, issue.Severity);
  }

  [Fact]
  public void Validate_WhenFrozenGraphIsConsistent_ReturnsOrderedValidReport()
  {
    var source = Node(new NodeId(1), new StableNodeAnchor(NLCPGNodeKind.SyntaxNode, 1, 0, 1, StableNodeRole.SyntaxNode, 0, 1));
    var target = Node(new NodeId(2), new StableNodeAnchor(NLCPGNodeKind.Operation, 1, 0, 1, StableNodeRole.Operation, 0, 2));
    var graph = NLCPGGraph.CreateFrozen(
      new[] { source, target },
      new[] { new NLCPGEdge(source.NodeId!.Value, target.NodeId!.Value, NLCPGEdgeKind.OpHasSyntax) });

    var report = new CpgGraphValidator().Validate(graph);

    Assert.True(report.IsValid);
    Assert.Empty(report.Issues);
  }

  [Fact]
  public void ValidatePersistedBuild_WhenBoundaryEdgeHasNoPrimaryOwner_ReportsStableCode()
  {
    var primary = CreateShard(
      new[] { new CpgFrozenNode(0, 1, "SyntaxNode", "fixture.cs", 0, 1, "SyntaxNode", null, null, null, false) });
    var boundary = CreateShard(
      Array.Empty<CpgFrozenNode>(),
      new[] { new CpgFrozenBoundaryEdge(1, 9, "CfgNext", null, null) },
      CpgShardRole.BoundaryAdjacency,
      new CpgBoundaryAdjacency("owner", CpgBoundaryAdjacencyDirection.Outgoing));

    var report = new CpgPersistedBuildValidator().Validate(new[]
    {
      new CpgBuildRoutingShardEntry(primary, Location("primary")),
      new CpgBuildRoutingShardEntry(boundary, Location("boundary")),
    });

    Assert.Contains(report.Issues, issue => issue.Code == "CPG104");
  }

  [Fact]
  public void ValidatePersistedBuild_WhenPrimaryNodeIsDuplicated_ReportsStableCode()
  {
    var first = CreateShard(
      new[]
      {
        new CpgFrozenNode(0, 1, "SyntaxNode", "fixture.cs", 0, 1, "SyntaxNode", null, null, null, false),
        new CpgFrozenNode(1, 1, "Operation", "fixture.cs", 0, 1, "Operation", null, null, null, false),
      });

    var report = new CpgPersistedBuildValidator().Validate(new[]
    {
      new CpgBuildRoutingShardEntry(first, Location("first")),
    });

    Assert.Contains(report.Issues, issue => issue.Code == "CPG100");
  }

  private static NLCPGNode Node(NodeId nodeId, StableNodeAnchor anchor)
  {
    return new NLCPGNode(
      NLCPGNodeKind.SyntaxNode,
      nameof(NLCPGNodeKind.SyntaxNode),
      FilePath: "fixture.cs",
      SpanStart: 0,
      SpanEnd: 1,
      NodeId: nodeId,
      StableAnchor: anchor);
  }

  private static CpgFrozenShard CreateShard(
    IReadOnlyList<CpgFrozenNode> nodes,
    IReadOnlyList<CpgFrozenBoundaryEdge>? boundaryEdges = null,
    CpgShardRole role = CpgShardRole.Primary,
    CpgBoundaryAdjacency? adjacency = null)
  {
    return new CpgFrozenShard(
      new CpgShardLookup(
        new CpgFileKey("project", "fixture.cs", "source"),
        new CpgFragmentKey(Guid.NewGuid().ToString("N"), 0, 1, "fragment"),
        1,
        "profile"),
      nodes,
      Array.Empty<CpgFrozenEdge>(),
      Array.Empty<CpgSymbolLocation>(),
      boundaryEdges,
      role,
      adjacency);
  }

  private static CpgShardLocation Location(string shardId)
  {
    return new CpgShardLocation(shardId, $"{shardId}.cpgbin", "hash", 1, CpgShardStatus.Complete);
  }
}
