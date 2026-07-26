using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DominancePassContractTests
{
  [Fact]
  public void BuildFromSource_DominanceBitSetOverlay_PreservesEdgeOrderAcrossRepeatedBuilds()
  {
    Assert.NotNull(typeof(RoslynCpgBuilder).Assembly.GetType(
      "MinimalRoslynCpg.Builder.RoslynCpgBuilder+BlockBitSet"));
    var expectedGraph = BuildGraph(maxDegreeOfParallelism: 1);
    var expectedDominanceEdges = FormatOverlayEdges(expectedGraph, RoslynCpgEdgeKind.Dominates);
    var expectedPostDominanceEdges = FormatOverlayEdges(expectedGraph, RoslynCpgEdgeKind.PostDominates);
    var expectedControlDependenceEdges = FormatOverlayEdges(
      expectedGraph,
      RoslynCpgEdgeKind.ControlDependence);

    Assert.NotEmpty(expectedDominanceEdges);
    Assert.NotEmpty(expectedPostDominanceEdges);
    Assert.NotEmpty(expectedControlDependenceEdges);

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var actualGraph = BuildGraph(maxDegreeOfParallelism);

      Assert.Equal(expectedDominanceEdges, FormatOverlayEdges(actualGraph, RoslynCpgEdgeKind.Dominates));
      Assert.Equal(
        expectedPostDominanceEdges,
        FormatOverlayEdges(actualGraph, RoslynCpgEdgeKind.PostDominates));
      Assert.Equal(
        expectedControlDependenceEdges,
        FormatOverlayEdges(actualGraph, RoslynCpgEdgeKind.ControlDependence));
    }
  }

  private static RoslynCpgGraph BuildGraph(int maxDegreeOfParallelism)
  {
    var options = new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 1,
      LargeFileMethodThreshold: 1,
      LargeMethodLineSpanThreshold: 1,
      RequestedCapabilities: new[]
      {
        RoslynCpgCapability.ControlDependence,
      });

    return new RoslynCpgBuilder(options).BuildFromSource(
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

  private static string[] FormatOverlayEdges(RoslynCpgGraph graph, RoslynCpgEdgeKind edgeKind)
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

  private static string DescribeNode(RoslynCpgGraph graph, RoslynCpgNode node)
  {
    var displayText = graph.GetDisplayText(node).Replace("\r\n", "\n", StringComparison.Ordinal);
    return string.Join(
      ":",
      node.Kind,
      node.Name,
      node.FullName,
      displayText);
  }

  private static RoslynCpgNode GetRequiredNode(RoslynCpgGraph graph, NodeId nodeId)
  {
    var node = graph.GetNode(nodeId);
    Assert.NotNull(node);
    return node!;
  }
}
