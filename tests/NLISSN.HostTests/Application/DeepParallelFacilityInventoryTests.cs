using System.Collections.Concurrent;
using NL.Concurrency;
using NLISSN.Application;
using NLISSN.Core.Pipeline;
using RoslynPrototype.Tests.TestCodeSet.Target;
using Xunit;

namespace RoslynPrototype.Tests.Application;

/// <summary>
/// G0-N 的**运行期**设施盘点：走一遍真实目录分析，记录每一层**实际**调用了哪个并行设施，
/// 并判定该调用是否发生在**内核工作项内部**（即「隐藏嵌套」/「额外内核」是否真实存在）。
/// <para>
/// 判定手段只用公开契约，不反射私有状态：在设施回调内部尝试
/// <c>scheduler.RunAsync(一个平凡提交)</c>。内核的「提交深度恒为 1」守卫会对
/// **任何位于工作项内的提交**抛出含 "flat" 的 <see cref="InvalidOperationException"/>，
/// 因此「会不会被拒」就是「是否身处内核工作项内」的可观测判据。
/// </para>
/// <para>
/// 这样做的价值：计划附录 A 此前只有**静态盘点**（读源码推断哪些层在核外）。
/// 本测试给出**运行期事实**，并会在将来某层被迁进内核、或新增核外并行时立刻变化。
/// </para>
/// </summary>
public sealed class DeepParallelFacilityInventoryTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Analyze_RealDirectoryPipeline_ReportsEveryParallelFacilityAndItsNestingState()
    {
        var options = new RoslynPrototypeExecutionOptions(
          DirectoryMaxDegreeOfParallelism: 3,
          CpgMaxDegreeOfParallelism: 3,
          GroupMaxDegreeOfParallelism: 3,
          HelperMaxDegreeOfParallelism: 3,
          ReplayMaxDegreeOfParallelism: 3,
          MaxConcurrentOperations: 3,
          EnableDirectoryParallelism: true,
          EnableGroupParallelism: true,
          EnableHelperParallelism: true);
        var workTelemetry = new WorkTelemetryCollector();
        var scheduler = new WorkScheduler(
          AnalysisRuntime.CreateSchedulerOptions(options),
          workTelemetry);

        // 池需要引用内核来做嵌套探测；内核先建，池后建，用晚期绑定打破循环。
        var pool = new NestingProbeConcurrencyPool(scheduler);
        var runtime = new AnalysisRuntime(
          options,
          new AnalysisEpoch(0, 0, 0),
          concurrencyPool: pool,
          scheduler: scheduler);

        var sources = new[]
        {
            new DirectorySourceFile(0, "a.cs", AtomicExpressionSources.AtomicNameSource),
            new DirectorySourceFile(1, "b.cs", AtomicExpressionSources.AtomicNameSource),
            new DirectorySourceFile(2, "c.cs", AtomicExpressionSources.AtomicNameSource)
        };

        var outcome = new DirectoryAnalysisUseCase(RulePipelineTestFactory.Create()).Analyze(
          sources,
          AnalysisLegacyOptionsTestExtensions.CreateSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["target-name"] = "s"
            }),
          runtime);

        await scheduler.DisposeAsync();

        // 分析本身必须成功——否则下面的"没有嵌套"没有意义。
        Assert.Equal(new[] { 0, 1, 2 }, outcome.FileResults.Select(file => file.Index));

        var inventory = pool.DescribeInventory();

        // 核心断言：真实管线中**没有任何**层在内核工作项内部调用旧并发设施。
        // 若此断言失败，说明出现了「隐藏嵌套」——正是 G0-N 要防的形态，
        // 而失败信息会直接点名是哪一层的哪个方法。
        Assert.True(
          pool.NestedObservations.IsEmpty,
          "检测到内核工作项内部的旧并发设施调用（隐藏嵌套）: "
          + string.Join("; ", pool.NestedObservations));

        // 同时固定：真实管线**确实**用到了旧池，否则本测试对"核外并行"就是空转。
        // 若将来全部迁入内核，这里会失败并迫使更新盘点结论（而不是悄悄变成空测试）。
        Assert.True(
          pool.TotalObservations > 0,
          $"真实目录管线未观测到任何旧并发设施调用；运行期盘点为空，需重新评估。遥测：{inventory}");

        // 内核本身也必须真的被用到（否则内核路径没被覆盖，本盘点不完整）。
        Assert.NotEmpty(workTelemetry.Records);

        // 把实测到的设施盘点落盘，作为「哪些层真的在核外并行」的运行期证据。
        // 该文件是**观测输出**，不是测试输入；写入失败不应掩盖断言结果，故吞掉异常。
        try
        {
            var evidencePath = Path.Combine(
              FindRepositoryRoot(),
              "Build",
              "g0m-calibration",
              "deep-facility-inventory.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            File.WriteAllText(
              evidencePath,
              $"inventory: {inventory}{Environment.NewLine}"
              + $"totalObservations: {pool.TotalObservations}{Environment.NewLine}"
              + $"nestedObservations: {pool.NestedObservations.Count}{Environment.NewLine}"
              + $"kernelSubmissions: {workTelemetry.Records.Count}{Environment.NewLine}"
              + "kernelCategories: "
              + string.Join(
                  ", ",
                  workTelemetry.Records
                    .GroupBy(record => record.Category, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => $"{group.Key}={group.Count()}"))
              + Environment.NewLine);
        }
        catch (IOException)
        {
            // 证据落盘失败不影响契约判定。
        }
    }

    /// <summary>
    /// 从当前目录向上寻找仓库根（以 <c>AGENTS.md</c> 与 <c>src</c> 同时存在为标志）。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录（需同时存在 AGENTS.md 与 src）。");
    }

    /// <summary>
    /// 装饰 <see cref="BoundedConcurrencyPool"/>，在每个回调内部做一次「是否身处内核工作项」探测，
    /// 并按方法名累计调用次数。
    /// </summary>
    private sealed class NestingProbeConcurrencyPool : IConcurrencyPool
    {
        private readonly IConcurrencyPool _inner = new BoundedConcurrencyPool();
        private readonly WorkScheduler _scheduler;
        private readonly ConcurrentDictionary<string, int> _observations = new(StringComparer.Ordinal);

        public NestingProbeConcurrencyPool(WorkScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        /// <summary>在设施回调内部观测到的「身处内核工作项」记录。</summary>
        public ConcurrentBag<string> NestedObservations { get; } = new();

        /// <summary>观测到的设施调用总次数（含未嵌套的）。</summary>
        public int TotalObservations => _observations.Values.Sum();

        /// <summary>按方法名汇总的调用次数，作为运行期设施盘点。</summary>
        public string DescribeInventory() =>
          string.Join(
            ", ",
            _observations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
              .Select(pair => $"{pair.Key}={pair.Value}"));

        private void Observe(string facility)
        {
            _observations.AddOrUpdate(facility, 1, static (_, current) => current + 1);
            if (IsInsideKernelWorkItem())
            {
                NestedObservations.Add(facility);
            }
        }

        /// <summary>
        /// 用公开契约判定当前是否身处内核工作项内部：在内核工作项内提交会被守卫拒绝。
        /// 带超时，避免在异常情形下挂住整个套件。
        /// </summary>
        private bool IsInsideKernelWorkItem()
        {
            try
            {
                var probe = _scheduler.RunAsync(new WorkSubmission<int>
                {
                    Items = new[]
                    {
                        new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(0) },
                    },
                });

                // 未被拒：等待它完成（平凡提交，正常极快）；超时则记为未知，不挂住套件。
                _ = probe.Wait(ProbeTimeout);
                return false;
            }
            catch (AggregateException aggregate)
              when (aggregate.InnerException is InvalidOperationException error &&
                    error.Message.Contains("flat", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        public Task<TResult> ExecuteWithAdmissionAsync<TResult>(
          ConcurrencyAdmissionRequest request,
          Func<CancellationToken, Task<TResult>> operation,
          CancellationToken cancellationToken = default)
        {
            Observe(nameof(ExecuteWithAdmissionAsync));
            return _inner.ExecuteWithAdmissionAsync(request, operation, cancellationToken);
        }

        public Task<IReadOnlyList<TResult>> SelectOrderedAsync<TResult>(
          int itemCount,
          int maxDegreeOfParallelism,
          Func<int, CancellationToken, Task<TResult>> workItem,
          CancellationToken cancellationToken = default)
        {
            Observe(nameof(SelectOrderedAsync));
            return _inner.SelectOrderedAsync(itemCount, maxDegreeOfParallelism, workItem, cancellationToken);
        }

        public Task<IReadOnlyList<TResult>> SelectOrderedAsync<TSource, TResult>(
          IReadOnlyList<TSource> sources,
          int maxDegreeOfParallelism,
          Func<TSource, int, CancellationToken, Task<TResult>> workItem,
          CancellationToken cancellationToken = default)
        {
            Observe(nameof(SelectOrderedAsync));
            return _inner.SelectOrderedAsync(sources, maxDegreeOfParallelism, workItem, cancellationToken);
        }

        public Task<IReadOnlyList<TResult>> SelectCpuBoundOrdered<TSource, TResult>(
          IReadOnlyList<TSource> sources,
          int maxDegreeOfParallelism,
          Func<TSource, int, CancellationToken, TResult> workItem,
          CancellationToken cancellationToken = default)
        {
            Observe(nameof(SelectCpuBoundOrdered));
            return _inner.SelectCpuBoundOrdered(sources, maxDegreeOfParallelism, workItem, cancellationToken);
        }

        public void CommitOrdered<TSource, TResult>(
          IReadOnlyList<TSource> sources,
          ConcurrencyWindowOptions options,
          Func<TSource, int, TResult> workItem,
          Action<TResult, int> commit,
          Func<TResult, int>? retainedRecordCount = null,
          CancellationToken cancellationToken = default)
        {
            Observe(nameof(CommitOrdered));
            _inner.CommitOrdered(sources, options, workItem, commit, retainedRecordCount, cancellationToken);
        }

        public void CommitTwoStageOrdered<TSource, TCollected, TPrepared, TResult>(
          IReadOnlyList<TSource> sources,
          ConcurrencyWindowOptions options,
          Func<TSource, int, TCollected> collect,
          Func<TCollected, int, TPrepared> prepare,
          Func<TPrepared, int, TResult> solve,
          Action<TResult, int> commit,
          Func<TCollected, int>? collectedRetainedRecordCount = null,
          Func<TResult, int>? resultRetainedRecordCount = null,
          CancellationToken cancellationToken = default)
        {
            Observe(nameof(CommitTwoStageOrdered));
            _inner.CommitTwoStageOrdered(
              sources,
              options,
              collect,
              prepare,
              solve,
              commit,
              collectedRetainedRecordCount,
              resultRetainedRecordCount,
              cancellationToken);
        }

        public Task ForEachAsync<TSource>(
          IReadOnlyList<TSource> sources,
          int maxDegreeOfParallelism,
          Func<TSource, int, CancellationToken, Task> workItem,
          CancellationToken cancellationToken = default)
        {
            // ⚠️ ForEachAsync 就是 ForEachScan 的落点（ParameterShrinkAnalyzer.cs:1357），
            // 也是计划附录 A 记为「深层调用的真实阻塞只剩 ForEachScan」的那一处。
            // 逐项探测：它可能在核外被调用，也可能在规则体（=内核工作项）内被调用。
            Observe(nameof(ForEachAsync));
            return _inner.ForEachAsync(sources, maxDegreeOfParallelism, workItem, cancellationToken);
        }

        public Task<DependencyExecutionResult<TNode, TResult>> RunDependencyGraphAsync<TNode, TResult>(
          IReadOnlyList<DependencyWorkItem<TNode, TResult>> workItems,
          int maxDegreeOfParallelism,
          IComparer<TNode> readyOrder,
          CancellationToken cancellationToken = default)
          where TNode : notnull
        {
            Observe(nameof(RunDependencyGraphAsync));
            return _inner.RunDependencyGraphAsync(
              workItems,
              maxDegreeOfParallelism,
              readyOrder,
              cancellationToken);
        }
    }
}
