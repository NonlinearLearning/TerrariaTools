using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgWorkBatchDataFlowTests
{
    private const string Source = """
      public sealed class Sample
      {
        private int _field;
        public int Value { get; set; }

        public int First(int value)
        {
          var total = value;
          for (var index = 0; index < 2; index++)
          {
            total += index;
          }

          return total;
        }

        public int Second(int value)
        {
          if (value > 0)
          {
            _field = value;
            Value = _field;
          }

          return Value;
        }

        public int Third(int value)
        {
          return First(value) + Second(value);
        }

        public int Fourth(int value)
        {
          Value = value;
          return Value;
        }
      }
      """;

    private const string BudgetSource = """
      public sealed class BudgetSample
      {
        public int Run(int value)
        {
          var result = value;
          result += 1;
          return result;
        }
      }
      """;

    private const string SecondSource = """
      public sealed class OtherSample
      {
        public int Only(int value)
        {
          var result = value;
          result += 1;
          return result;
        }
      }
      """;

    [Fact]
    public void BuildFromSource_DataFlowWorkBatch_ReportsBatchAndWorkerTelemetry()
    {
        var builder = new NLCPGBuilder(CreateOptions(4));

        _ = builder.BuildFromSource(Source, "dataflow-workbatch-telemetry.cs");

        Assert.Equal(1, builder.LastBuildMetrics.DataFlowBatchCount);
        Assert.Equal(4, builder.LastBuildMetrics.DataFlowWorkerCount);
        Assert.Equal(4, builder.LastBuildMetrics.DataFlowMethodMetrics!.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    public void BuildFromSource_BatchedDataFlow_PreservesGraphAcrossDegreesOfParallelism(int dop)
    {
        var baseline = new NLCPGBuilder(CreateOptions(1))
          .BuildFromSource(Source, "dataflow-workbatch-equivalence.cs");
        var actual = new NLCPGBuilder(CreateOptions(dop))
          .BuildFromSource(Source, "dataflow-workbatch-equivalence.cs");

        Assert.Equal(DescribeGraph(baseline), DescribeGraph(actual));
        Assert.Contains(actual.Edges, edge => edge.Kind == NLCPGEdgeKind.DataFlow);
    }

    [Fact]
    public void BuildFromSource_DataFlowCandidateBudget_SkipMethodPreservesNonFlowGraph()
    {
        var options = CreateOptions(4) with
        {
            DataFlowOptions = new NLCPGDataFlowOptions(
              MaxDefinitionsPerMethod: int.MaxValue,
              MaxFlowNodesPerMethod: int.MaxValue,
              MaxCandidateEdgesPerMethod: 0,
              OverflowBehavior: NLCPGDataFlowOverflowBehavior.SkipMethod),
        };

        var builder = new NLCPGBuilder(options);
        var graph = builder.BuildFromSource(BudgetSource, "dataflow-workbatch-skip.cs");

        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.DataFlow);
        Assert.Contains(builder.LastBuildMetrics.DataFlowMethodMetrics!, metric =>
          metric.OverflowReason == NLCPGDataFlowOverflowReason.CandidateEdgeLimitExceeded);
    }

    [Fact]
    public void BuildFromSource_DataFlowCandidateBudget_FailBuildPreservesOverflowFailure()
    {
        var options = CreateOptions(4) with
        {
            DataFlowOptions = new NLCPGDataFlowOptions(
              MaxDefinitionsPerMethod: int.MaxValue,
              MaxFlowNodesPerMethod: int.MaxValue,
              MaxCandidateEdgesPerMethod: 0,
              OverflowBehavior: NLCPGDataFlowOverflowBehavior.FailBuild),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
          new NLCPGBuilder(options).BuildFromSource(BudgetSource, "dataflow-workbatch-fail.cs"));

        Assert.Contains("Data-flow", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 多文件批次下，<c>DataFlowPass</c> 写出的每条方法指标都必须带上**自己的**源文件路径。
    /// </summary>
    /// <remarks>
    /// 这是"逐文件过滤"能成立的前提：mapper 按 <c>SourceFilePath</c> 归属，
    /// 而归属键只有 <c>DataFlowPass</c> 在归并进 builder 级列表时才知道。
    /// 若该字段漏填（全为 null），mapper 会静默丢弃全部样本——报告不再失真，
    /// 而是**整段消失**，比原来更难发现，故必须在此锁住非空且互不相同。
    /// </remarks>
    [Fact]
    public void BuildManyDocuments_StampsEachMethodMetricWithItsOwningFile()
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(Source, path: "first.cs"),
            CSharpSyntaxTree.ParseText(SecondSource, path: "second.cs"),
        };
        var compilation = CSharpCompilation.Create(
          assemblyName: "DataFlowMethodAttribution",
          syntaxTrees: trees,
          references: NLCPGBuilder.CreateMetadataReferences());
        var documents = new[]
        {
            new NLCPGBuildDocument(
              "first.cs", Source, compilation.GetSemanticModel(trees[0]), trees[0].GetRoot()),
            new NLCPGBuildDocument(
              "second.cs", SecondSource, compilation.GetSemanticModel(trees[1]), trees[1].GetRoot()),
        };

        var builder = new NLCPGBuilder(CreateOptions(4));
        _ = builder.BuildManyDocuments(documents);

        var metrics = builder.LastBuildMetrics.DataFlowMethodMetrics!;
        Assert.NotEmpty(metrics);

        // 归属键必须已填：任何一条为 null 都会被 mapper 判为"来源不明"而丢弃。
        Assert.DoesNotContain(metrics, metric => metric.SourceFilePath is null);

        // 且必须**各归各的**：两个文件都有方法，不能整批记到同一个文件上。
        var byFile = metrics
          .GroupBy(metric => metric.SourceFilePath!, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(new[] { "first.cs", "second.cs" }, byFile.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(4, byFile["first.cs"]);
        Assert.Equal(1, byFile["second.cs"]);
    }

    private static NLCPGBuilderOptions CreateOptions(int dop)
    {
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
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
            graph.ResolveName(node),
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
