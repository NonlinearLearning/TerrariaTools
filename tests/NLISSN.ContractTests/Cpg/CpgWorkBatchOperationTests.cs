using NLCPG.Builder;
using NLCPG.Contracts;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgWorkBatchOperationTests
{
    private const string Source = """
      public sealed class Sample
      {
        public int First(int value)
        {
          var current = value;
          for (var index = 0; index < 4; index++)
          {
            current = current + index;
          }

          return current;
        }

        public int Second(int value) => First(value) + 1;
      }
      """;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    public void BatchedOperationCollection_PreservesGraphSignature(int dop)
    {
        var baseline = Build(1);
        var actual = Build(dop);

        Assert.Equal(Describe(baseline), Describe(actual));
    }

    [Fact]
    public void BatchedOperationCollection_PreservesOperationInventoryForDeepTrees()
    {
        var source = "public sealed class Sample { public int Run(int value) { " +
          string.Concat(Enumerable.Range(0, 40).Select(index => $"value = value + {index};")) +
          " return value; } }";

        var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 4,
        }).BuildFromSource(source, "deep-operation.cs");

        Assert.True(graph.Nodes.Count(node => node.Kind.ToString().StartsWith("Op", StringComparison.Ordinal)) >= 40);
    }

    private static NLCPG.Model.NLCPGGraph Build(int dop)
    {
        return new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
        }).BuildFromSource(Source, "batch-operation.cs");
    }

    private static string[] Describe(NLCPG.Model.NLCPGGraph graph)
    {
        var nodes = graph.Nodes.Select(node => string.Join(
          "|",
          node.NodeId,
          node.Kind,
          graph.ResolveDisplayKind(node),
          graph.ResolveName(node),
          graph.ResolveFullName(node),
          graph.ResolveSignature(node),
          graph.ResolveFilePath(node),
          node.SpanStart,
          node.SpanEnd));
        var edges = graph.Edges.Select(edge => string.Join(
          "|",
          edge.SourceNodeId,
          edge.TargetNodeId,
          edge.Kind,
          edge.ContextId,
          edge.StructuredLabel?.StableKey));
        return nodes.Concat(edges).OrderBy(item => item, StringComparer.Ordinal).ToArray();
    }
}
