using System.Diagnostics;
using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// <summary>
/// 守住内核的 worker 数量上界（G0-M 的"超大项策略"在内核配置面上的落点）。
/// <para>
/// <c>WorkSchedulerOptions.WorkerCount</c> 取六个额度字段的**最大值**，
/// 而 <c>WorkScheduler.EnsureWorkersStarted</c>（<c>WorkScheduler.cs:192-195</c>）
/// 在首次提交时按该值 <c>Task.Run</c> 出**等量**的长期 worker：
/// </para>
/// <code>
/// for (var index = 0; index < _options.WorkerCount; index++)
///     _workers.Add(Task.Run(WorkerLoopAsync));
/// </code>
/// <para>
/// 六个字段在 <see cref="WorkSchedulerOptions"/> 中**都不校验**，
/// YAML（<c>YamlConfigurationLoader</c>）与 JSON Schema 也只要求 <c>&gt;= 1</c>。
/// 故一个合法配置值就能让首次提交排队上亿个任务。
/// </para>
/// <para>
/// 同仓库对同类字段已有封顶先例：<c>ProjectExportOptions.cs:72</c> 用
/// <c>Math.Min(12, Math.Max(1, MaxDegreeOfParallelism))</c> 把导出 worker 封顶 12，
/// CLI 参考中写明"实际上限为 12"。内核此前缺这一层。
/// </para>
/// <para>
/// ⚠️ <b>判别力边界（不得外推）：</b>本文件不断言"实际有多少物理线程"——
/// 内核 worker 在 <c>await gate.Task</c> 上让出，是**异步**的，
/// 线程计数无法判别 worker 数量（见轮次 4 的否证结论）。
/// 这里断言的是**配置校验**与**启动代价**这两件可观测的事。
/// </para>
/// </summary>
public sealed class WorkSchedulerWorkerBoundTests
{
    private const int Huge = 200_000_000;

    private static WorkSchedulerOptions CreateOptions(int limit)
    {
        return new WorkSchedulerOptions(limit, limit, limit, limit, limit, limit);
    }

    [Fact]
    public void WorkScheduler_WhenALimitExceedsTheCeiling_FailsDiagnosablyBeforeStartingAnyWorker()
    {
        // 无上界时：构造成功，然后在首次提交时排队两亿个 Task —— 必然资源耗尽，
        // 且用户拿不到任何指向配置字段的错误。
        var options = CreateOptions(Huge);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new WorkScheduler(options));

        // 消息必须点名**具体字段**与**实际值**，否则用户无法定位是哪一项配错了。
        Assert.Equal("DirectoryLimit", exception.ParamName);
        Assert.Contains(Huge.ToString(System.Globalization.CultureInfo.InvariantCulture),
          exception.Message, StringComparison.Ordinal);
        Assert.Contains(
          WorkSchedulerOptions.MaximumWorkerCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
          exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkScheduler_WhenALimitIsExactlyTheCeiling_StillConstructs()
    {
        // 边界内保护：上界本身必须可用，否则就是引入了新的失败面。
        var options = CreateOptions(WorkSchedulerOptions.MaximumWorkerCount);

        var scheduler = new WorkScheduler(options);
        scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();

        Assert.Equal(WorkSchedulerOptions.MaximumWorkerCount, options.WorkerCount);
    }

    [Fact]
    public void WorkerCount_WhenLimitsAreWithinTheCeiling_IsNotClamped()
    {
        // 反例保护：正常配置不得被上界改动，否则就是静默改变语义。
        var options = CreateOptions(8);

        Assert.Equal(8, options.WorkerCount);
    }

    [Fact]
    public void ResolveLimit_WhenLimitIsLargeButSupported_StillReportsTheConfiguredCategoryLimit()
    {
        // 上界只作用于**非法配置的拒绝**，不得改写各**类别额度**本身。
        // 否则注入内核后 SchedulerOptions 上报的生效值会与配置不符
        // （R4 明确要求日志打印实际生效的类别上限）。
        var options = CreateOptions(512);

        Assert.Equal(512, options.ResolveLimit(WorkCategories.Cpg));
    }

    [Fact]
    public void RunAsync_WithALargeButSupportedLimit_CompletesInsteadOfQueuingMillionsOfTasks()
    {
        // 端到端形态：1024 个长期 worker 必须能正常起来并完成工作。
        var options = CreateOptions(WorkSchedulerOptions.MaximumWorkerCount);
        var scheduler = new WorkScheduler(options);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var run = scheduler.RunAsync(new WorkSubmission<int>
            {
                Items = new[]
                {
                    new WorkItem<int>
                    {
                        StableOrder = 0,
                        ExecuteAsync = (_, _) => Task.FromResult(42),
                    },
                },
            });

            var finished = Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30))).GetAwaiter().GetResult();
            stopwatch.Stop();

            Assert.Same(run, finished);
            Assert.Equal(new[] { 42 }, run.GetAwaiter().GetResult());
            Assert.True(
              stopwatch.Elapsed < TimeSpan.FromSeconds(30),
              $"starting {options.WorkerCount} workers took {stopwatch.Elapsed}.");
        }
        finally
        {
            scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
