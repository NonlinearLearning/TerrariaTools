using NLCPG.Builder;
using NLCPG.Builder.Concurrency;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P 附录 R.3「分层」的验证：**L1 计算层只算 fragment，不写共享图**。
/// <para>
/// <b>为什么必须单独验证：</b>四要素中 ④「分层与窗口外依赖」的 <b>L0</b>（规划层只读）
/// 已由机制强制（附录 W，见 <c>PlanningLayerReadOnlyContractTests</c>），但 <b>L1</b>
/// 此前<b>只是一句注释</b>——例如 <c>PartitionedSyntaxPass</c> 写着
/// "不创建图节点，避免 worker 线程污染共享状态"。那正是附录 N.4/P.4 的
/// "<b>声明代替机制</b>"形态：worker 真的去写共享图时，没有任何东西会失败，
/// 除非某个测试恰好断言了图内容。
/// </para>
/// <para>
/// 故本文件测两件互不可替代的事（与 L0 那份同构）：
/// </para>
/// <list type="number">
/// <item><b>守卫真的会拒绝</b>：worker 计算窗口内写共享图必须抛出。</item>
/// <item><b>窗口真的开过</b>：真实构建必须进入该窗口；否则守卫是死代码，
/// 与"没有守卫"在观测上不可区分（附录 X.5/Y.4 同型教训）。</item>
/// </list>
/// <para>
/// 另有两件本层<b>特有</b>的、必须钉死的性质——它们的反向都是"看起来能用"的错误设计：
/// worker 内新建私有 <c>localGraph</c> 必须<b>允许</b>（否则打断三个阶段）；
/// reducer 回调内写共享图必须<b>允许</b>（那正是 L2 的职责）。
/// </para>
/// </summary>
public sealed class WorkerComputeLayerContractTests
{
    private const string Source = """
        namespace Demo;

        public sealed class Counter
        {
            public int Value { get; set; }

            public int Bump(int delta) => Value + delta;
        }

        public sealed class Caller
        {
            public int Run(Counter counter, int seed)
            {
                var total = 0;
                for (var i = 0; i < seed; i += 1)
                {
                    if (i % 2 == 0)
                    {
                        total += counter.Bump(i);
                    }
                    else
                    {
                        total -= counter.Value;
                    }
                }

                return total;
            }
        }
        """;

    /// <summary>
    /// 守卫本身：窗口内对**本图**构图必须抛出。
    /// <para>若此用例通过而窗口从不被真实构建进入，则守卫是死代码——故下一条用例不可省。</para>
    /// </summary>
    [Fact]
    public void EnterWorkerComputeWindow_ThenAddNode_Throws()
    {
        var graph = new NLCPGGraph();
        graph.EnterWorkerComputeWindow();
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
              () => graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.M")));

            Assert.Contains("L1 计算层", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            graph.ExitWorkerComputeWindow();
        }
    }

    /// <summary>边写入同样被拦——守卫在 <c>EnsureMutable</c> 收口，不依赖某个具体入口。</summary>
    [Fact]
    public void EnterWorkerComputeWindow_ThenAddEdge_Throws()
    {
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.A"));
        var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.B"));

        graph.EnterWorkerComputeWindow();
        try
        {
            Assert.Throws<InvalidOperationException>(
              () => graph.AddEdge(source, target, NLCPGEdgeKind.CallTargets));
        }
        finally
        {
            graph.ExitWorkerComputeWindow();
        }
    }

    /// <summary>
    /// <b>本层特有性质 1</b>：窗口是<b>按图实例</b>的。
    /// <para>
    /// worker 内会新建私有 <c>localGraph</c> 并写它（ControlFlow/ControlDependence/Dominance
    /// 各一处），那是 fragment 的构造过程，属 L1 的<b>职责</b>。若守卫做成
    /// "worker 期间禁写任何图"，会当场打断这三个阶段——本用例把该边界钉成事实。
    /// </para>
    /// </summary>
    [Fact]
    public void SharedGraphInWorkerWindow_DoesNotBlockWritesToIndependentGraph()
    {
        var shared = new NLCPGGraph();
        var local = new NLCPGGraph();

        shared.EnterWorkerComputeWindow();
        try
        {
            // 不得抛出。
            local.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.Local"));
        }
        finally
        {
            shared.ExitWorkerComputeWindow();
        }

        Assert.Single(local.Nodes);
    }

    /// <summary>
    /// <b>本层特有性质 2</b>：窗口<b>按线程</b>隔离，故 reducer（L2）不受 worker 影响。
    /// <para>
    /// reducer 与 worker <b>并发</b>运行（executor 先起 reducerTask 再起 workers），
    /// 而 reducer 正是唯一允许写共享图的 L2。若窗口用"图级计数"，DataFlow 的合法归并
    /// 写入会被误报——那比没有守卫更糟，因为诊断信息会把排查引向"谁在写图"这个
    /// 根本不存在的问题（与 L0 那次 <c>SnapshotMutableFacts</c> 误报同型）。
    /// </para>
    /// </summary>
    [Fact]
    public void WorkerWindowOnOneThread_DoesNotBlockWritesFromAnotherThread()
    {
        var graph = new NLCPGGraph();
        graph.EnterWorkerComputeWindow();
        try
        {
            // 另一个线程扮演 reducer：必须能够写图。
            var error = Record.Exception(
              () => Task.Run(() => graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.Reduced")))
                .GetAwaiter()
                .GetResult());

            Assert.Null(error);

            // 且 worker 线程自身仍处于窗口内、仍被拦——否则上面的放行只是"窗口没生效"。
            Assert.True(graph.IsInWorkerComputeWindow);
            Assert.Throws<InvalidOperationException>(
              () => graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.Worker")));
        }
        finally
        {
            graph.ExitWorkerComputeWindow();
        }
    }

    /// <summary>退出窗口后必须恢复可写——否则 <c>finally</c> 失配会把后续合法构图全部误报。</summary>
    [Fact]
    public void ExitWorkerComputeWindow_RestoresMutability()
    {
        var graph = new NLCPGGraph();
        graph.EnterWorkerComputeWindow();
        graph.ExitWorkerComputeWindow();

        Assert.False(graph.IsInWorkerComputeWindow);
        graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.AfterWindow"));

        Assert.Single(graph.Nodes);
    }

    /// <summary>
    /// <b>排除死代码</b>：真实构建必须进入 worker 计算窗口。
    /// <para>
    /// 这是本文件最重要的一条。<see cref="NLCPGGraph.IsInWorkerComputeWindow"/> 与
    /// <see cref="NLCPGGraph.WorkerComputeWindowEntryCount"/> 用途不同：前者只说"此刻"，
    /// 后者才证明"曾经开过"。若把钩子从 executor 移除，上面所有用例<b>仍然全绿</b>
    /// （因为没人进窗口就没人被拦），故必须用计数把"窗口真的开过"钉成事实。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_EntersTheWorkerComputeWindow()
    {
        var builder = Build();
        var graph = builder.BuildFromSource(Source, "worker-window.cs");

        Assert.True(
          graph.WorkerComputeWindowEntryCount > 0,
          "真实构建必须进入 L1 worker 计算窗口；计数为 0 说明守卫从未执行（死代码）。");
    }

    /// <summary>
    /// 规划覆盖的 5 个阶段都会被 executor 执行，故窗口进入次数必须显著大于 1
    /// （每个 batch 一次）。这同时排除"只在某个边角路径进过一次"的假象。
    /// </summary>
    [Fact]
    public void BuildFromSource_EntersTheWindowOncePerExecutedBatch()
    {
        var builder = Build();
        var graph = builder.BuildFromSource(Source, "worker-window-per-batch.cs");

        // 上限：进入次数不应多于"所有阶段 batch 数之和"——只做下界断言，
        // 因为 batch 切分数是实现细节，把它写成等式会把无关变更变成红灯。
        Assert.True(
          graph.WorkerComputeWindowEntryCount >= 2,
          $"窗口进入次数应至少等于被执行阶段数级别，实际 {graph.WorkerComputeWindowEntryCount}。");
    }

    /// <summary>
    /// 端到端：<b>真实构建全程不得有 worker 写共享图</b>。
    /// <para>
    /// 若上述静态审计（8 个 worker 委托都只产只读事实）有误，本用例会失败。
    /// 它与"计数 &gt; 0"合起来构成完整判据：守卫在做判定，且从未被触发。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WithAllPlanningCapabilities_NoWorkerWritesTheSharedGraph()
    {
        var builder = Build();
        var graph = builder.BuildFromSource(Source, "worker-window-purity.cs");

        Assert.NotNull(graph);
        Assert.True(graph.WorkerComputeWindowEntryCount > 0);
        // 构建成功后窗口必须已全部退出，否则 finally 失配（后续任何构图都会误报）。
        Assert.False(graph.IsInWorkerComputeWindow);
    }

    /// <summary>
    /// 直接驱动 executor：窗口钩子必须在 <c>processBatch</c> <b>期间</b>生效、在返回后解除。
    /// <para>
    /// 这一条不经过 builder，故能把"钩子接错位置"（例如放在 reduce 之后）单独暴露出来。
    /// </para>
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_InvokesWindowAroundEachBatch()
    {
        var graph = new NLCPGGraph();
        var observedInsideWindow = 0;
        var batchesProcessed = 0;
        var executor = new CpgWorkBatchExecutor(
          new CpgWorkBatchExecutorOptions(maxDegreeOfParallelism: 1),
          concurrencyPool: null,
          workerComputeWindowEnter: graph.EnterWorkerComputeWindow,
          workerComputeWindowExit: graph.ExitWorkerComputeWindow);

        await executor.ExecuteAsync(
          CreateBatches(3),
          (batch, _, _) =>
          {
              Interlocked.Increment(ref batchesProcessed);
              if (graph.IsInWorkerComputeWindow)
              {
                  Interlocked.Increment(ref observedInsideWindow);
              }

              // worker 内写共享图必须抛出。
              Assert.Throws<InvalidOperationException>(
                () => graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, $"Demo.M{batch.StableOrder}")));
              return batch.StableOrder;
          },
          stageId: "CPG.WorkBatch.WorkerWindowContract");

        Assert.Equal(3, batchesProcessed);
        Assert.Equal(3, observedInsideWindow);
        Assert.Equal(3, graph.WorkerComputeWindowEntryCount);
        Assert.False(graph.IsInWorkerComputeWindow);
    }

    /// <summary>
    /// reducer 回调（L2）在<b>同一图</b>上写图必须被允许——它是唯一写图者。
    /// <para>
    /// 这是"守卫没有把 L2 一起拦掉"的证据；若窗口按图级计数，本用例会失败。
    /// </para>
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AllowsTheReducerToWriteTheSharedGraph()
    {
        var graph = new NLCPGGraph();
        var reduced = 0;
        var executor = new CpgWorkBatchExecutor(
          new CpgWorkBatchExecutorOptions(maxDegreeOfParallelism: 1),
          concurrencyPool: null,
          workerComputeWindowEnter: graph.EnterWorkerComputeWindow,
          workerComputeWindowExit: graph.ExitWorkerComputeWindow);

        await executor.ExecuteAsync(
          CreateBatches(2),
          (batch, _, _) => batch.StableOrder,
          reduceResult: order =>
          {
              graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, $"Demo.Reduced{order}"));
              reduced += 1;
          },
          stageId: "CPG.WorkBatch.WorkerWindowReducerContract");

        Assert.Equal(2, reduced);
        Assert.Equal(2, graph.Nodes.Count);
    }

    /// <summary>未接钩子时（钩子为 null）行为必须与从前完全一致，零副作用。</summary>
    [Fact]
    public async Task ExecuteAsync_WithoutWindowHook_BehavesAsBefore()
    {
        var graph = new NLCPGGraph();
        // 刻意用单 worker：本用例要证明"无钩子时不拦"，若用多 worker 并发写
        // 同一个**非线程安全**的构图态图，会丢写（实测 3 次 AddNode 只留 2 个节点）——
        // 那恰好印证了本守卫要防的正是这种竞争；用例本身不应制造它。
        var executor = new CpgWorkBatchExecutor(
          new CpgWorkBatchExecutorOptions(maxDegreeOfParallelism: 1));

        var results = await executor.ExecuteAsync(
          CreateBatches(3),
          (batch, _, _) =>
          {
              // 无钩子时 worker 内写图**不被拦**（本守卫不做任何事）——证明钩子为可选。
              graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, $"Demo.NoHook{batch.StableOrder}"));
              return batch.StableOrder;
          },
          stageId: "CPG.WorkBatch.NoWindowContract");

        Assert.Equal(new[] { 0, 1, 2 }, results.OrderBy(order => order));
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(0, graph.WorkerComputeWindowEntryCount);
    }

    private static NLCPGBuilder Build() =>
      new(NLCPGBuilderOptions.CreateDefault() with
      {
          MaxDegreeOfParallelism = 1,
          RequestedCapabilities =
          [
              NLCPGCapability.CallTargets,
              NLCPGCapability.Cfg,
              NLCPGCapability.MethodModel,
              NLCPGCapability.Dominance,
              NLCPGCapability.DataFlow,
          ],
      });

    private static IReadOnlyList<CpgWorkBatch> CreateBatches(int count)
    {
        return Enumerable.Range(0, count)
          .Select(index => new CpgWorkBatch(
            index,
            "worker-window.cs",
            index,
            new[]
            {
                new CpgWorkItem(index, "worker-window.cs", $"M{index}", index, index + 1, 1, CpgWorkItemKind.Method),
            },
            1,
            1,
            64,
            CpgWorkBatchKind.Methods))
          .ToArray();
    }
}
