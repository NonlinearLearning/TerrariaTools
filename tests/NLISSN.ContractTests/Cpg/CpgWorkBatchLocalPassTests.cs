using System.Text;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgWorkBatchLocalPassTests
{
    private const string Source = """
      public sealed class Sample
      {
        private int _field;
        public int Value { get; set; }

        public int Run(int value)
        {
          try
          {
            if (value > 0)
            {
              _field = value;
              Value = _field;
            }
            else
            {
              Value = 0;
            }
          }
          catch
          {
            Value = -1;
          }

          return Value;
        }
      }
      """;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    public void BatchedLocalPasses_PreserveCfgAndMemberFacts(int dop)
    {
        var baseline = Build(1);
        var actual = Build(dop);

        Assert.Equal(
          DescribeEdges(baseline, NLCPGEdgeKind.CfgNext, NLCPGEdgeKind.CfgTrue, NLCPGEdgeKind.CfgFalse, NLCPGEdgeKind.AccessesMember, NLCPGEdgeKind.Ref),
          DescribeEdges(actual, NLCPGEdgeKind.CfgNext, NLCPGEdgeKind.CfgTrue, NLCPGEdgeKind.CfgFalse, NLCPGEdgeKind.AccessesMember, NLCPGEdgeKind.Ref));
    }

    [Fact]
    public void BatchedLocalPasses_LeaveUnresolvedDynamicAccessWithoutGuessedMemberEdge()
    {
        const string source = "public sealed class Sample { public object Run(dynamic value) => value.Missing; }";
        var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault()).BuildFromSource(source, "dynamic-member.cs");

        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.AccessesMember);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    public void BatchedDominanceBarrier_PreservesDominanceAndControlDependenceFacts(int dop)
    {
        var options = NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
            RequestedCapabilities = new[]
            {
                NLCPGCapability.Dominance,
                NLCPGCapability.ControlDependence,
            },
        };
        var graph = new NLCPGBuilder(options).BuildFromSource(Source, "dominance-batch.cs");

        Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.Dominates);
        Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.PostDominates);
        Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.ControlDependence);
    }

    /// <summary>
    /// CreateOwned 的所有权契约在生产路径上的证据：同一文件被切成多个批，
    /// 每个批各自把独占数组交给一个 fragment，最终图必须与整文件单批 DOP=1 基线
    /// 逐节点、**逐边按枚举序**等价。
    /// <para>
    /// 若某个生产者在自己交出数组后仍继续写入、或跨批复用了同一数组，被污染的批
    /// 会丢失/错配自己的节点与边，本断言即失败。因此本用例同时覆盖「所有权确实
    /// 跨批转移」与「接管后无写别名」。
    /// </para>
    /// <para>
    /// ⚠️ 边界：ControlFlow 阶段未向执行器传 resultMetrics，故
    /// <c>OutputNodeCount</c> 恒为 <c>CpgWorkBatchResultMetrics.Empty</c>，**不能**用作
    /// 逐批产出判据。这里改用执行器真实填充的 <c>InputCount</c> 证明分批形态，
    /// 逐批产出的判据交给与单批基线的全签名比较。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void BatchedLocalPasses_WhenOwnedFragmentsSpanMultipleBatches_KeepEveryBatchIsolated(int dop)
    {
        // 200 个方法 / 64 方法每批 ⇒ 至少 3 个批，所有权转移真正跨批发生。
        const int methodCount = 200;
        var source = CreateManyMethodSource(methodCount);
        var sink = new RecordingWorkBatchPerformanceEventSink();
        var options = MultiBatchOptions(dop) with { WorkBatchPerformanceEventSink = sink };

        var graph = new NLCPGBuilder(options).BuildFromSource(source, "owned-batches.cs");

        // 前置非空转：ControlFlow 阶段必须真的产生了多个批，
        // 否则「跨批隔离」就是恒真的空断言。
        var controlFlowEvents = sink.Events
          .Where(performanceEvent =>
            performanceEvent.StageId == CpgWorkBatchPerformanceStageId.ControlFlow)
          .ToArray();
        var batchIds = controlFlowEvents.Select(performanceEvent => performanceEvent.BatchId).Distinct().ToArray();
        Assert.True(
          batchIds.Length > 1,
          $"ControlFlow 阶段未产生多个批，跨批隔离断言会退化为恒真：batchIds=[{string.Join(", ", batchIds)}]");

        // 每个批都真的承载了方法输入，故每个批都产出了自己的 fragment。
        Assert.All(controlFlowEvents, performanceEvent =>
          Assert.True(performanceEvent.InputCount > 0, $"批 {performanceEvent.BatchId} 没有任何方法输入。"));
        Assert.Equal(methodCount, controlFlowEvents.Sum(performanceEvent => performanceEvent.InputCount));

        var baseline = new NLCPGBuilder(MultiBatchOptions(1) with
        {
            WorkBatchMaxMethodsPerBatch = int.MaxValue,
        }).BuildFromSource(source, "owned-batches.cs");

        Assert.NotEmpty(baseline.Nodes);
        Assert.NotEmpty(baseline.Edges);
        Assert.Equal(GraphSignatureOf(baseline), GraphSignatureOf(graph));
    }

    /// <summary>
    /// 两个可信生产者必须走接管入口，而不是回退到复制入口。
    /// </summary>
    /// <remarks>
    /// 这条断言为什么必须读源码：两条入口产出**逐字段、逐顺序完全相同**的 fragment
    /// （见 <c>CreateOwned_WhenGivenEquivalentCollections_MatchesPublicConstructor</c>），
    /// 因此任何行为测试都无法区分生产者究竟调了哪一个。若只保留行为测试，
    /// 有人把生产者改回 <c>new LocalCpgFragment(...)</c> 会悄悄恢复重复载荷复制，
    /// 而全部测试依然通过——本用例正是为堵住这个盲区而存在。
    /// </remarks>
    [Fact]
    public void LocalFragmentProducers_UseOwnedFactoryInsteadOfDefensiveCopyConstructor()
    {
        string[][] migratedProducers =
        [
          ["src", "NLCPG", "Builder", "Passes", "ControlFlowPass.cs"],
          ["src", "NLCPG", "Builder", "Passes", "ControlDependencePass.cs"],
        ];

        foreach (var relativePath in migratedProducers)
        {
            var source = File.ReadAllText(ProjectPath(relativePath));
            Assert.Contains("LocalCpgFragment.CreateOwned(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new LocalCpgFragment(", source, StringComparison.Ordinal);
        }

        // 范围守卫：DominancePass 明确留在复制入口（本里程碑只迁移两个生产者）。
        // 若以后把它也迁移，这里会失败并提醒同步更新范围说明，而不是静默漂移。
        var dominanceSource = File.ReadAllText(
          ProjectPath("src", "NLCPG", "Builder", "Passes", "DominancePass.cs"));
        Assert.Contains("new LocalCpgFragment(", dominanceSource, StringComparison.Ordinal);
    }

    private static string ProjectPath(params string[] parts)
    {
        var sourceFile = new System.Diagnostics.StackTrace(true).GetFrames()?
          .Select(frame => frame.GetFileName())
          .First(path => !string.IsNullOrWhiteSpace(path));
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile!)!);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, Path.Combine(parts));
    }

    private static NLCPGBuilderOptions MultiBatchOptions(int dop)
    {
        // 打开大文件阈值，使分批路径生效而不是走小文件快路径。
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
            RequestedCapabilities = new[] { NLCPGCapability.All },
            WorkBatchMaxMethodsPerBatch = 64,
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
        };
    }

    private static string CreateManyMethodSource(int methodCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("public sealed class WideSample");
        builder.AppendLine("{");
        for (var index = 0; index < methodCount; index++)
        {
            builder.AppendLine($"  public int M{index:D3}(int value)");
            builder.AppendLine("  {");
            builder.AppendLine("    if (value > 0)");
            builder.AppendLine("    {");
            builder.AppendLine("      return value + 1;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    return value;");
            builder.AppendLine("  }");
            builder.AppendLine();
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    /// <summary>
    /// 逐节点（按 NodeId）加**逐边按枚举序**的签名。边序是判据的一部分，故此处不排序。
    /// </summary>
    private static string GraphSignatureOf(NLCPG.Model.NLCPGGraph graph)
    {
        var nodes = graph.Nodes
          .OrderBy(node => node.NodeId)
          .Select(node => string.Join(
            "|",
            node.NodeId,
            node.Kind,
            node.StableAnchor,
            node.SpanStart,
            node.SpanEnd));
        var edges = graph.Edges.Select(edge => string.Join(
          "|",
          edge.SourceNodeId,
          edge.TargetNodeId,
          edge.Kind,
          edge.StructuredLabel?.StableKey,
          edge.ContextId));

        return string.Join(Environment.NewLine, nodes.Concat(edges));
    }

    private static NLCPG.Model.NLCPGGraph Build(int dop)
    {
        return new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
        }).BuildFromSource(Source, "local-passes.cs");
    }

    private static string[] DescribeEdges(NLCPG.Model.NLCPGGraph graph, params NLCPGEdgeKind[] kinds)
    {
        var selectedKinds = kinds.ToHashSet();
        return graph.Edges
          .Where(edge => selectedKinds.Contains(edge.Kind))
          .Select(edge => string.Join("|", edge.SourceNodeId, edge.TargetNodeId, edge.Kind))
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
    }

    private sealed class RecordingWorkBatchPerformanceEventSink : ICpgWorkBatchPerformanceEventSink
    {
        private readonly List<CpgWorkBatchPerformanceEvent> _events = [];

        public IReadOnlyList<CpgWorkBatchPerformanceEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToArray();
                }
            }
        }

        public void Record(CpgWorkBatchPerformanceEvent performanceEvent)
        {
            lock (_events)
            {
                _events.Add(performanceEvent);
            }
        }
    }
}
