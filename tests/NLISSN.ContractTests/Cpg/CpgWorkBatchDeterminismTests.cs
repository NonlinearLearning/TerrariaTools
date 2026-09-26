using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgWorkBatchDeterminismTests
{
    private const string Source = """
      public sealed class DeterministicSample
      {
        public int Produce(int value)
        {
          var result = value;
          if (value > 0)
          {
            result += 1;
          }
          else
          {
            result -= 1;
          }

          return result;
        }

        public int Consume(int value)
        {
          return Produce(value);
        }
      }
      """;

    [Fact]
    public void BuildFromSource_AllWorkBatchStages_PreserveGraphAndFrozenIndex()
    {
        var signatures = new[] { 1, 2, 16 }
          .Select(dop => Build(dop))
          .ToArray();

        Assert.All(signatures, signature => Assert.True(signature.HasQueryIndex));
        Assert.All(signatures, signature => Assert.NotEmpty(signature.CallSiteNodeIds));
        Assert.All(signatures, signature => Assert.NotEmpty(signature.ControlFlowNodeIds));
        AssertEquivalent(signatures[0], signatures[1]);
        AssertEquivalent(signatures[0], signatures[2]);
    }

    private static void AssertEquivalent(GraphSignature expected, GraphSignature actual)
    {
        Assert.Equal(expected.HasQueryIndex, actual.HasQueryIndex);
        Assert.Equal(expected.CallSiteNodeIds, actual.CallSiteNodeIds);
        Assert.Equal(expected.ControlFlowNodeIds, actual.ControlFlowNodeIds);
        Assert.Equal(expected.GraphItems, actual.GraphItems);
    }

    private static GraphSignature Build(int dop)
    {
        var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
            RequestedCapabilities = new[]
            {
                NLCPGCapability.All,
            },
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
        }).BuildFromSource(Source, "workbatch-determinism.cs");

        return new GraphSignature(
          graph.HasQueryIndex,
          graph.NodesByKind(NLCPGNodeKind.CallSite).Select(node => node.NodeId!.Value).ToArray(),
          graph.NodesByKind(NLCPGNodeKind.OpBlock).Select(node => node.NodeId!.Value).ToArray(),
          graph.Nodes
            .OrderBy(node => node.NodeId)
            .Select(node => $"{node.NodeId}|{node.Kind}|{graph.ResolveFullName(node)}|{node.SpanStart}|{node.SpanEnd}")
            .Concat(graph.Edges
              .OrderBy(edge => edge.SourceNodeId)
              .ThenBy(edge => edge.Kind)
              .ThenBy(edge => edge.TargetNodeId)
              .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}"))
            .ToArray());
    }

    private sealed record GraphSignature(
      bool HasQueryIndex,
      IReadOnlyList<NodeId> CallSiteNodeIds,
      IReadOnlyList<NodeId> ControlFlowNodeIds,
      IReadOnlyList<string> GraphItems);
}
