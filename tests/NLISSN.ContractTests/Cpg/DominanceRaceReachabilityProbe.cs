using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// 回归：在**生产默认分批配置**（<c>WorkBatchMaxMethodsPerBatch = 64</c>）下，
/// 方法数 &gt; 64 的文件会让多个 work-batch worker 并发进入 `GetOrCreateOperationNode`。
/// <para>
/// 修复前 <c>_operationNodesByOperation</c>（普通 <c>Dictionary</c>）会被并发写入，
/// 抛 <c>InvalidOperationException: Operations that change non-concurrent collections
/// must have exclusive access</c>。现由 builder 级缓存门串行化读—改—写，故：
/// ① 不得崩溃；② 同一输入跨 DOP/多次构建必须给出同一 <c>GraphSnapshotVersion</c>。
/// </para>
/// <para>
/// 该用例同时守住 T6 的「不得靠改回串行掩盖竞态」要求：断言
/// <c>WorkBatchParallelismObserved</c>，确保并行 work-batch 路径仍被真正走到。
/// </para>
/// </summary>
public sealed class DominanceRaceReachabilityProbe
{
    private static string BuildSource(int methodCount)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("public sealed class WideSample");
        builder.AppendLine("{");
        for (var index = 0; index < methodCount; index++)
        {
            // 每个方法都含跨方法调用与控制流，确保 dominance pass 需要映射 operation 节点。
            var next = (index + 1) % methodCount;
            builder.AppendLine(
              $"  public int M{index:D3}(int value) {{ if (value > {index}) {{ return M{next:D3}(value) + {index}; }} return value - {index}; }}");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    [Theory]
    [InlineData(8, 70)]
    [InlineData(8, 200)]
    [InlineData(16, 200)]
    [InlineData(16, 400)]
    public void BuildFromSource_WithMoreMethodsThanBatchSize_AtProductionDefaults(int dop, int methodCount)
    {
        // 实测（2026-09-24，修复前）：dop=8/methods=200、dop=16/methods=200、dop=16/methods=400
        // 三次均复现；dop=8/methods=70 未复现（批次太少，worker 重叠窗口不足）。
        // methodCount > 64（默认每批方法数）⇒ 必然产生多个方法批 ⇒ 多 worker 并发进入本路径。
        var source = BuildSource(methodCount);
        string? baselineSnapshotVersion = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
            {
                MaxDegreeOfParallelism = dop,
                // 刻意【不】设置 WorkBatchMaxMethodsPerBatch，使用生产默认 64。
                RequestedCapabilities = new[] { NLCPGCapability.All },
                LargeFileLineThreshold = 1,
                LargeFileMethodThreshold = 1,
                LargeMethodLineSpanThreshold = 1,
                SyntaxLargeFileLineThreshold = 1,
            }).BuildFromSource(source, "wide-production-default.cs");

            Assert.NotNull(graph);
            Assert.NotEmpty(graph.Nodes);
            Assert.NotEmpty(graph.Edges);

            // 并行安全性之外的确定性要求：同一输入必须稳定收敛到同一快照版本。
            baselineSnapshotVersion ??= graph.GraphSnapshotVersion;
            Assert.Equal(baselineSnapshotVersion, graph.GraphSnapshotVersion);
        }
    }

    /// <summary>
    /// T6 的反串行化守卫：验收明确禁止「靠改回串行掩盖竞态」。
    /// <para>
    /// 本用例读取 Dominance 阶段的 WorkBatch 遥测，断言该阶段*确实*由多个 worker
    /// 并发执行。若有人把并行路径退回串行（或把整个 stage 放进一把大锁），
    /// <c>DistinctWorkers</c> 会退化为 1、<c>PeakActiveWorkerCount</c> 会退化为 1，
    /// 本用例即失败。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDominanceRunsAtHighDop_StillExecutesOnMultipleWorkers()
    {
        var sink = new RecordingWorkBatchPerformanceEventSink();
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 8,
            // 小批 ⇒ 方法批数远多于 worker 数，确保有并发重叠窗口。
            WorkBatchMaxMethodsPerBatch = 16,
            RequestedCapabilities = new[] { NLCPGCapability.All },
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
            WorkBatchPerformanceEventSink = sink,
            PerformanceRunId = "t6-anti-serialization",
        });

        var graph = builder.BuildFromSource(BuildSource(methodCount: 200), "wide-anti-serialization.cs");
        Assert.NotEmpty(graph.Nodes);

        var dominanceEvents = sink.Events
          .Where(item => item.StageId == CpgWorkBatchPerformanceStageId.Dominance)
          .ToArray();

        Assert.NotEmpty(dominanceEvents);

        // 这两条断言就是「没有偷偷改回串行」的证据。
        var distinctWorkers = dominanceEvents.Select(item => item.WorkerIndex).Distinct().Count();
        var peakActive = dominanceEvents.Max(item => item.PeakActiveWorkerCount);
        Assert.True(
          distinctWorkers > 1,
          $"Dominance 阶段只观察到 {distinctWorkers} 个 worker，疑似已退回串行执行。");
        Assert.True(
          peakActive > 1,
          $"Dominance 阶段 PeakActiveWorkerCount={peakActive}，疑似已退回串行执行。");
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
