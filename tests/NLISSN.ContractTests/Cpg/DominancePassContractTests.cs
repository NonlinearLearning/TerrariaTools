using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DominancePassContractTests
{
  [Fact]
  public void BuildFromSource_DominanceBitSetOverlay_PreservesEdgeOrderAcrossRepeatedBuilds()
  {
    Assert.NotNull(typeof(NLCPGBuilder).Assembly.GetType(
      "NLCPG.Builder.NLCPGBuilder+BlockBitSet"));
    var expectedGraph = BuildGraph(maxDegreeOfParallelism: 1);
    var expectedDominanceEdges = FormatOverlayEdges(expectedGraph, NLCPGEdgeKind.Dominates);
    var expectedPostDominanceEdges = FormatOverlayEdges(expectedGraph, NLCPGEdgeKind.PostDominates);
    var expectedControlDependenceEdges = FormatOverlayEdges(
      expectedGraph,
      NLCPGEdgeKind.ControlDependence);

    Assert.NotEmpty(expectedDominanceEdges);
    Assert.NotEmpty(expectedPostDominanceEdges);
    Assert.NotEmpty(expectedControlDependenceEdges);

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var actualGraph = BuildGraph(maxDegreeOfParallelism);

      Assert.Equal(expectedDominanceEdges, FormatOverlayEdges(actualGraph, NLCPGEdgeKind.Dominates));
      Assert.Equal(
        expectedPostDominanceEdges,
        FormatOverlayEdges(actualGraph, NLCPGEdgeKind.PostDominates));
      Assert.Equal(
        expectedControlDependenceEdges,
        FormatOverlayEdges(actualGraph, NLCPGEdgeKind.ControlDependence));
    }
  }

  private static NLCPGGraph BuildGraph(int maxDegreeOfParallelism)
  {
    var options = new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 1,
      LargeFileMethodThreshold: 1,
      LargeMethodLineSpanThreshold: 1,
      RequestedCapabilities: new[]
      {
        NLCPGCapability.ControlDependence,
      });

    return new NLCPGBuilder(options).BuildFromSource(
      """
      namespace Demo;

      public sealed class DominanceSample
      {
        public int Linear(int value)
        {
          var total = value;
          total += 1;
          return total;
        }

        public int BranchLoopAndUnreachable(int value)
        {
          var total = value;
          if (total > 0)
          {
            total += 1;
          }
          else
          {
            total -= 1;
          }

          while (total < 10)
          {
            total += 2;
            if (total == 5)
            {
              return total;
            }
          }

          return total;
          total += 100;
        }
      }
      """,
      $"dominance-contract-dop-{maxDegreeOfParallelism}.cs");
  }

  private static string[] FormatOverlayEdges(NLCPGGraph graph, NLCPGEdgeKind edgeKind)
  {
    return graph.Edges
      .Where(edge => edge.Kind == edgeKind)
      .Select(edge => string.Join(
        "|",
        edge.Kind,
        DescribeNode(graph, GetRequiredNode(graph, edge.SourceNodeId)),
        DescribeNode(graph, GetRequiredNode(graph, edge.TargetNodeId))))
      .ToArray();
  }

  private static string DescribeNode(NLCPGGraph graph, NLCPGNode node)
  {
    var displayText = graph.GetDisplayText(node).Replace("\r\n", "\n", StringComparison.Ordinal);
    return string.Join(
      ":",
      node.Kind,
      node.Name,
      node.FullName,
      displayText);
  }

  private static NLCPGNode GetRequiredNode(NLCPGGraph graph, NodeId nodeId)
  {
    var node = graph.GetNode(nodeId);
    Assert.NotNull(node);
    return node!;
  }
}
