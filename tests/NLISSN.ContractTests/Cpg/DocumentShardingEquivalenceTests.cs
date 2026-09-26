using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// Gate G4：分片等价性入口验证（设计 §6.3、执行计划 Task S4-3）。
/// <para>
/// 目的：证明「把同一文件的**方法集合切成不同批次/分片**并改变调度度」与
/// 「不切分、DOP=1」产出的图**逐节点且逐边同序**等价。
/// </para>
/// <para>
/// ⚠️ 已知陷阱：既有 oracle 对**边序是盲的**——<c>CpgExecutionSnapshotComparer</c>
/// 会把两侧都 <c>OrderBy</c> 再比较（见该类第 32-33 行），
/// <c>CpgShardBuildCoordinatorTests.ExactEdges</c> 同样先排序。
/// 本测试**不排序**，直接按 <see cref="NLCPGGraph.Edges"/> 的枚举序比较，
/// 否则等于把边序差异掩盖掉。
/// </para>
/// </summary>
public sealed class DocumentShardingEquivalenceTests
{
    /// <summary>含 ≥8 个方法、含跨方法调用与控制流的固定源。</summary>
    private const string Source = """
      public sealed class ShardedSample
      {
        public int M01(int value) => M02(value) + 1;
        public int M02(int value) => M03(value) + M04(value);
        public int M03(int value)
        {
          var total = 0;
          for (var index = 0; index < value; index++)
          {
            total += M05(index);
          }

          return total;
        }

        public int M04(int value) => value > 0 ? M06(value) : M07(value);
        public int M05(int value) => value * 2;
        public int M06(int value) => M08(value) - 1;
        public int M07(int value) => -value;
        public int M08(int value)
        {
          if (value > 10)
          {
            return M05(value);
          }

          return value;
        }
      }
      """;

    /// <summary>不切分基线：整文件一个方法批，DOP=1。</summary>
    private static GraphSignature BuildBaseline()
    {
        return Build(maxMethodsPerBatch: int.MaxValue, maxDegreeOfParallelism: 1);
    }

    private static GraphSignature Build(
      int maxMethodsPerBatch,
      int maxDegreeOfParallelism,
      ICpgWorkBatchPerformanceEventSink? workBatchSink = null)
    {
        // 打开大文件阈值，使分区/分批路径真正生效，而不是走小文件快路径。
        var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            WorkBatchMaxMethodsPerBatch = maxMethodsPerBatch,
            RequestedCapabilities = new[] { NLCPGCapability.All },
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
            WorkBatchPerformanceEventSink = workBatchSink,
        }).BuildFromSource(Source, "sharding-equivalence.cs");

        return SignatureOf(graph);
    }

    /// <summary>
    /// 逐节点、**逐边按枚举序**的签名。边序是判据的一部分，故此处**不得**排序。
    /// </summary>
    private static GraphSignature SignatureOf(NLCPGGraph graph)
    {
        return new GraphSignature(
          graph.GraphSnapshotVersion,
          graph.Nodes
            .OrderBy(node => node.NodeId)
            .Select(node =>
              $"{node.NodeId}|{node.Kind}|{graph.ResolveFullName(node)}|{node.SpanStart}|{node.SpanEnd}|{node.StableAnchor}")
            .ToArray(),
          // 关键：保持 graph.Edges 的插入序，不做任何 OrderBy。
          graph.Edges
            .Select(edge =>
              $"{edge.SourceNodeId}>{edge.Kind}>{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}|{edge.ContextId}")
            .ToArray());
    }

    [Fact]
    public void BuildFromSource_WhenMethodBatchSizeVaries_ActuallyChangesBatchPartitioning()
    {
        // 前置校验：若 maxMethodsPerBatch 对分批**没有实际影响**，
        // 那么「分批不同但结果等价」就是恒真的空断言，Gate G4 也就毫无证明力。
        // 本测试先证明该旋钮真的改变了分批形态。
        var batched = new List<int>();
        foreach (var maxMethodsPerBatch in new[] { 1, 2, 4, 8 })
        {
            var sink = new RecordingWorkBatchPerformanceEventSink();
            Build(maxMethodsPerBatch, maxDegreeOfParallelism: 1, sink);
            var batchCount = sink.Events.Select(performanceEvent => performanceEvent.BatchId).Distinct().Count();
            batched.Add(batchCount);
        }

        // 每个方法单独成批 ⇒ 批数应多于一个方法一批 ⇒ 批数确实随该旋钮变化。
        Assert.True(
          batched[0] > batched[^1],
          $"maxMethodsPerBatch 未改变分批形态，等价性断言会退化为恒真：batchCounts=[{string.Join(", ", batched)}]");
    }

    [Fact]
    public void BuildFromSource_WhenMethodBatchSizeVaries_PreservesNodeAndEdgeOrder()
    {
        var baseline = BuildBaseline();

        // 断言基线本身有内容，否则下面的等价性会退化为「空 == 空」而恒真。
        Assert.NotEmpty(baseline.Nodes);
        Assert.NotEmpty(baseline.EdgeSequenceInEnumerationOrder);

        foreach (var maxMethodsPerBatch in new[] { 1, 2, 4, 8 })
        {
            var sharded = Build(maxMethodsPerBatch, maxDegreeOfParallelism: 1);
            AssertEquivalent(baseline, sharded, $"methodsPerBatch={maxMethodsPerBatch}");
        }
    }

    /// <summary>
    /// DOP 维度等价性。
    /// <para>
    /// ⚠️ **本用例证明的不是「并行 dominance 等价」**：本配置每次批方法数为
    /// `int.MaxValue` ⇒ 每个阶段只有 **1 个批** ⇒ dominance 阶段实测
    /// `maxPeakActive=1`（即使 DOP=8 也是串行），故该路径**不**覆盖并发 dominance 竞态。
    /// 它有效的部分是：DOP 变化下**节点与边序仍保持**——实测在 DOP=4/8 时
    /// `ControlDependence` 阶段确实产生了 2 个批、2 个 worker
    /// （`distinctWorkers=[0,1]`, `maxPeakActive=2`），该路径保序成立。
    /// </para>
    /// <para>
    /// 真正的并行 dominance 等价性由
    /// <see cref="BuildFromSource_WhenShardedAndParallel_PreservesNodeAndEdgeOrder"/>
    /// 与 <c>DominanceRaceReachabilityProbe</c> 覆盖（竞态修复后已解除 Skip）。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDegreeOfParallelismVaries_PreservesNodeAndEdgeOrder()
    {
        var baseline = BuildBaseline();

        foreach (var dop in new[] { 2, 4, 8 })
        {
            var concurrent = Build(maxMethodsPerBatch: int.MaxValue, maxDegreeOfParallelism: dop);
            AssertEquivalent(baseline, concurrent, $"dop={dop}");
        }
    }

    /// <summary>多批 + 多 worker 组合维度：共享缓存已加锁后，该路径应保持逐节点与逐边同序。</summary>
    [Fact]
    public void BuildFromSource_WhenShardedAndParallel_PreservesNodeAndEdgeOrder()
    {
        var baseline = BuildBaseline();

        // 组合维度：小批 + 高并发，最接近未来分片形态。
        foreach (var (maxMethodsPerBatch, dop) in new[] { (1, 4), (2, 4), (1, 8), (4, 8) })
        {
            var sharded = Build(maxMethodsPerBatch, dop);
            AssertEquivalent(baseline, sharded, $"methodsPerBatch={maxMethodsPerBatch},dop={dop}");
        }
    }

    [Fact]
    public void AssertEquivalent_WhenOnlyEdgeOrderDiffers_MustFail()
    {
        // 本测试证明 oracle 对**边序敏感**——这是 Gate G4 的核心要求。
        // 既有 oracle（CpgExecutionSnapshotComparer）会把两侧排序后比较，
        // 因此对边序完全盲。若本测试失败，说明我的 oracle 也退化了，
        // 那么上面几条「等价性通过」就没有证明力。
        var nodes = new[] { "n1", "n2" };
        var edges = new[] { "a>b:Kind", "b>c:Kind" };

        var baseline = new GraphSignature("v1", nodes, edges);
        // 节点集与边**集合**完全相同，仅**顺序**颠倒。
        var reordered = new GraphSignature("v1", nodes, edges.Reverse().ToArray());

        Assert.ThrowsAny<Exception>(() => AssertEquivalent(baseline, reordered, "order-only"));
    }

    /// <summary>
    /// 最接近真实分片的对照：`StreamingMode: true` 走的是
    /// **分片发布**路径（`SkeletonShardPublisher` + `CpgShardBuildSession`），
    /// 而非单纯的本地方法分批。
    /// <para>
    /// 既有测试 `CpgShardBuildCoordinatorTests.BuildFromSource_StreamingPersistence_MatchesSerialSnapshotAtConfiguredDop`
    /// 只比较 `GraphSnapshotVersion`，对节点集/边序**不敏感**；本测试补上逐节点与**逐边同序**比较。
    /// </para>
    /// </summary>
    /// <summary>
    /// 单线程下的分片发布等价性：DOP=1 ⇒ 无 worker 重叠 ⇒ 最保守的基线证据。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenStreamingShardPublicationEnabledAtDop1_PreservesNodeAndEdgeOrder()
    {
        AssertStreamingEquivalent(maxDegreeOfParallelism: 1);
    }

    /// <summary>
    /// 并行分片发布：与 <see cref="BuildFromSource_WhenDegreeOfParallelismVaries_PreservesNodeAndEdgeOrder"/>
    /// 同为 Gate G4 的并行维度证据。共享缓存加锁后竞态已消除，故解除 Skip。
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void BuildFromSource_WhenStreamingShardPublicationEnabledWithParallelism_PreservesNodeAndEdgeOrder(
      int maxDegreeOfParallelism)
    {
        AssertStreamingEquivalent(maxDegreeOfParallelism);
    }

    private static void AssertStreamingEquivalent(int maxDegreeOfParallelism)
    {
        var baseline = BuildBaseline();
        Assert.NotEmpty(baseline.Nodes);
        Assert.NotEmpty(baseline.EdgeSequenceInEnumerationOrder);

        var root = Path.Combine(
          Path.GetTempPath(),
          "g4-streaming-shard-equivalence",
          Guid.NewGuid().ToString("N"));
        try
        {
            var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                WorkBatchMaxMethodsPerBatch = 2,
                RequestedCapabilities = new[] { NLCPGCapability.All },
                LargeFileLineThreshold = 1,
                LargeFileMethodThreshold = 1,
                LargeMethodLineSpanThreshold = 1,
                SyntaxLargeFileLineThreshold = 1,
                Persistence = new CpgPersistenceOptions(
                  root,
                  $"g4-profile-dop-{maxDegreeOfParallelism}",
                  StreamingMode: true),
            }).BuildFromSource(Source, "sharding-equivalence.cs");

            // 先证明分片确实发生了，否则「等价」可能只是没走分片路径。
            Assert.True(
              Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories).Any(),
              "未产生任何分片文件，StreamingMode 未真正生效，本次等价性断言不成立。");

            AssertEquivalent(baseline, SignatureOf(graph), $"streaming,dop={maxDegreeOfParallelism}");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void AssertEquivalent(GraphSignature expected, GraphSignature actual, string scenario)
    {
        Assert.Equal(expected.GraphSnapshotVersion, actual.GraphSnapshotVersion);

        // 节点：逐条比较（两侧都已按 NodeId 排序，是「集合」语义）。
        Assert.Equal(expected.Nodes, actual.Nodes);

        // 边：**必须逐位置比较**，不能排序后比较，否则边序差异被掩盖。
        if (!expected.EdgeSequenceInEnumerationOrder.SequenceEqual(
              actual.EdgeSequenceInEnumerationOrder, StringComparer.Ordinal))
        {
            Assert.Fail(DescribeEdgeOrderDifference(expected, actual, scenario));
        }
    }

    /// <summary>
    /// 差异必须逐条可读：先报告「集合是否相同」，以区分
    /// 「只是边序不同」与「边集本身不同」，再给出首个位置差异。
    /// </summary>
    private static string DescribeEdgeOrderDifference(
      GraphSignature expected,
      GraphSignature actual,
      string scenario)
    {
        var expectedSet = expected.EdgeSequenceInEnumerationOrder
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
        var actualSet = actual.EdgeSequenceInEnumerationOrder
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
        var setEqual = expectedSet.SequenceEqual(actualSet, StringComparer.Ordinal);

        var firstDivergence = -1;
        var shared = Math.Min(
          expected.EdgeSequenceInEnumerationOrder.Count,
          actual.EdgeSequenceInEnumerationOrder.Count);
        for (var index = 0; index < shared; index++)
        {
            if (!string.Equals(
                  expected.EdgeSequenceInEnumerationOrder[index],
                  actual.EdgeSequenceInEnumerationOrder[index],
                  StringComparison.Ordinal))
            {
                firstDivergence = index;
                break;
            }
        }

        var lines = new List<string>
        {
            $"场景 {scenario}：边序不等价（Gate G4 FAIL）。",
            $"基线边数={expected.EdgeSequenceInEnumerationOrder.Count}，" +
              $"对照边数={actual.EdgeSequenceInEnumerationOrder.Count}。",
            $"边**集合**是否相同={setEqual}（false 表示边集本身也不同，问题比边序更严重）。",
        };

        if (firstDivergence >= 0)
        {
            lines.Add($"首个位置差异 index={firstDivergence}：");
            lines.Add($"  基线: {expected.EdgeSequenceInEnumerationOrder[firstDivergence]}");
            lines.Add($"  对照: {actual.EdgeSequenceInEnumerationOrder[firstDivergence]}");
        }

        if (!setEqual)
        {
            lines.Add("仅存在于基线的前 10 条：");
            lines.AddRange(expectedSet.Except(actualSet, StringComparer.Ordinal).Take(10).Select(v => $"  {v}"));
            lines.Add("仅存在于对照的前 10 条：");
            lines.AddRange(actualSet.Except(expectedSet, StringComparer.Ordinal).Take(10).Select(v => $"  {v}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record GraphSignature(
      string GraphSnapshotVersion,
      IReadOnlyList<string> Nodes,
      IReadOnlyList<string> EdgeSequenceInEnumerationOrder);

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
