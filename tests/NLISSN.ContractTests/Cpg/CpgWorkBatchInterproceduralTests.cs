using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgWorkBatchInterproceduralTests
{
    private const string Source = """
      using System;

      public sealed class Sample
      {
        public int Add(int value)
        {
          var current = value;
          return current;
        }

        public int Overload(int value)
        {
          return Add(value);
        }

        public int Overload(string value)
        {
          return value.Length;
        }

        public int Caller(int value)
        {
          int Local(int input) => Add(input);
          return Local(value) + Overload(value);
        }

        public int Recursive(int value)
        {
          if (value <= 0)
          {
            return 0;
          }

          return Recursive(value - 1) + 1;
        }

        public int External(int value)
        {
          return Math.Abs(value);
        }

        public int Unresolved(dynamic value)
        {
          return value.Missing(value);
        }
      }
      """;

    [Fact]
    public void BuildFromSource_InterproceduralWorkBatch_PublishesOnlyAfterCallGraphBarrier()
    {
        var builder = new NLCPGBuilder(CreateOptions(4));
        var graph = builder.BuildFromSource(Source, "interprocedural-workbatch.cs");

        Assert.True(builder.LastBuildMetrics.CallGraphBatchCount > 0);
        Assert.True(builder.LastBuildMetrics.InterproceduralBarrierCompleted);
        Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.CallTargets);
        Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow);
        Assert.All(graph.Edges.Where(edge =>
          edge.Kind is NLCPGEdgeKind.CallTargets or NLCPGEdgeKind.InterproceduralDataFlow), edge =>
        {
            Assert.True(graph.GetNode(edge.SourceNodeId).HasValue);
            Assert.True(graph.GetNode(edge.TargetNodeId).HasValue);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    public void BuildFromSource_InterproceduralWorkBatch_PreservesGraphAcrossDegreesOfParallelism(int dop)
    {
        var baseline = new NLCPGBuilder(CreateOptions(1))
          .BuildFromSource(Source, "interprocedural-workbatch-equivalence.cs");
        var actual = new NLCPGBuilder(CreateOptions(dop))
          .BuildFromSource(Source, "interprocedural-workbatch-equivalence.cs");

        Assert.Equal(DescribeGraph(baseline), DescribeGraph(actual));
    }

    private static NLCPGBuilderOptions CreateOptions(int dop)
    {
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
            RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow },
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
        };
    }

    private static string[] DescribeGraph(NLCPGGraph graph)
    {
        var nodes = graph.Nodes
          .OrderBy(node => node.NodeId)
          .Select(node => string.Join(
            "|",
            node.NodeId,
            node.Kind,
            graph.ResolveFullName(node),
            node.SpanStart,
            node.SpanEnd))
          .ToArray();
        var edges = graph.Edges
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind)
          .ThenBy(edge => edge.TargetNodeId)
          .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
          .ToArray();
        return nodes.Concat(edges).ToArray();
    }
}
