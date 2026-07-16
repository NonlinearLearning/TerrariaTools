using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RoslynCpgNodeIdContractTests
{
  [Fact]
  public void DeterministicNodeIdTable_Create_AssignsStableIdsIndependentOfInputOrder()
  {
    var first = new StableNodeAnchor(
      RoslynCpgNodeKind.SyntaxNode,
      FilePathId: 2,
      SpanStart: 10,
      SpanEnd: 12,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 1);
    var second = new StableNodeAnchor(
      RoslynCpgNodeKind.CallSite,
      FilePathId: 1,
      SpanStart: 8,
      SpanEnd: 8,
      StableNodeRole.CallSite,
      Ordinal: 0,
      ExtraKeyId: 3);
    var third = new StableNodeAnchor(
      RoslynCpgNodeKind.Method,
      FilePathId: 1,
      SpanStart: 0,
      SpanEnd: 20,
      StableNodeRole.Method,
      Ordinal: 0,
      ExtraKeyId: 2);

    var ordered = DeterministicNodeIdTable.Create(new[] { first, second, third });
    var shuffled = DeterministicNodeIdTable.Create(new[] { third, first, second });
    var expectedOrder = new[] { first, second, third }
      .OrderBy(anchor => anchor.Kind)
      .ThenBy(anchor => anchor.FilePathId)
      .ThenBy(anchor => anchor.SpanStart)
      .ThenBy(anchor => anchor.SpanEnd)
      .ThenBy(anchor => anchor.Role)
      .ThenBy(anchor => anchor.Ordinal)
      .ThenBy(anchor => anchor.ExtraKeyId)
      .ToArray();

    Assert.Equal(ordered.Snapshot(), shuffled.Snapshot());
    for (var index = 0; index < expectedOrder.Length; index += 1)
    {
      Assert.Equal(new NodeId((uint)index + 1), ordered.Snapshot()[expectedOrder[index]]);
    }
  }

  [Fact]
  public void AddNodeAndEdge_BackfillsNodeIdsWithoutChangingDisplayFields()
  {
    var graph = new RoslynCpgGraph();
    var source = new RoslynCpgNode(RoslynCpgNodeKind.Operation, "Operation", Name: "source");
    var sink = new RoslynCpgNode(RoslynCpgNodeKind.Operation, "Operation", Name: "sink");

    var materializedSource = graph.AddNode(source);
    var materializedSink = graph.AddNode(sink);
    graph.AddEdge(
      materializedSource,
      materializedSink,
      RoslynCpgEdgeKind.DataFlow,
      contextId: new RoslynCpgContextId("flow"));
    graph.FreezeQueryIndex();

    var edge = Assert.Single(graph.Edges);
    materializedSource = Assert.Single(graph.Nodes, node => node.Name == "source");
    materializedSink = Assert.Single(graph.Nodes, node => node.Name == "sink");
    Assert.NotNull(materializedSource);
    Assert.NotNull(materializedSink);
    Assert.Equal("source", materializedSource.Name);
    Assert.Equal("sink", materializedSink.Name);
    Assert.Equal(new NodeId(1), materializedSource.NodeId);
    Assert.Equal(new NodeId(2), materializedSink.NodeId);
    Assert.NotNull(materializedSource.StableAnchor);
    Assert.NotNull(materializedSink.StableAnchor);
    Assert.Equal(materializedSource.NodeId!.Value, edge.SourceNodeId);
    Assert.Equal(materializedSink.NodeId!.Value, edge.TargetNodeId);
    Assert.Equal(materializedSource, graph.GetNode(materializedSource.NodeId!.Value));
    Assert.Equal("source", graph.GetNode(materializedSource.NodeId.Value)!.Name);
  }

  [Fact]
  public void BuildFromSource_RepeatedBuilds_PreserveLegacyToNodeIdMapping()
  {
    var baseline = BuildStableNodeKeyToNodeIdMap(CreateBuilder(maxDegreeOfParallelism: 1));

    for (var iteration = 0; iteration < 3; iteration += 1)
    {
      var candidate = BuildStableNodeKeyToNodeIdMap(CreateBuilder(maxDegreeOfParallelism: 1));
      Assert.Equal(baseline, candidate);
    }
  }

  [Fact]
  public void BuildFromSource_DifferentDegreesOfParallelism_PreserveLegacyToNodeIdMapping()
  {
    var baseline = BuildStableNodeKeyToNodeIdMap(CreateBuilder(maxDegreeOfParallelism: 1));

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var candidate = BuildStableNodeKeyToNodeIdMap(CreateBuilder(maxDegreeOfParallelism));
      Assert.Equal(baseline, candidate);
    }
  }

  private static Dictionary<string, uint> BuildStableNodeKeyToNodeIdMap(RoslynCpgBuilder builder)
  {
    var graph = builder.BuildFromSource(
      """
      namespace Demo;

      public sealed class Sample
      {
        private int _offset;

        public int Run(int seed)
        {
          var local = seed + _offset;
          if (local > 0)
          {
            return Helper(local);
          }

          return local - 1;
        }

        private int Helper(int value) => value + 1;
      }
      """,
      "nodeid-stability.cs");

    return graph.Nodes
      .OrderBy(BuildNodeContractKey, StringComparer.Ordinal)
      .ToDictionary(
        BuildNodeContractKey,
        node => Assert.NotNull(node.NodeId).Value,
        StringComparer.Ordinal);
  }

  private static string BuildNodeContractKey(RoslynCpgNode node)
  {
    return string.Join(
      "|",
      node.Kind,
      node.DisplayKind,
      node.Name,
      node.FullName,
      node.Signature,
      node.FilePath,
      node.SpanStart,
      node.SpanEnd,
      node.IsImplicit);
  }

  private static RoslynCpgBuilder CreateBuilder(int maxDegreeOfParallelism)
  {
    return new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 1,
      LargeFileMethodThreshold: 1,
      LargeMethodLineSpanThreshold: 1,
      SyntaxPassMode: RoslynCpgSyntaxPassMode.Partitioned,
      SyntaxLargeFileLineThreshold: 1));
  }
}
