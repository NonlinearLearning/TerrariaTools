using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NL.Concurrency;
using NLISSN.Application;
using NLISSN.Core.Analysis;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests.Application;

/// <summary>
/// 轮次 13：补上轮次 12 明确承认的缺口——**`ForEachScan` 从未被覆盖**。
/// <para>
/// 轮次 12 的运行期设施盘点里 `ForEachAsync` 计数为 0，因为最小目录夹具
/// （3 个原子表达式源文件）走不到参数收缩所需的**索引器**语法形态。
/// 本测试直接以**索引器**为入口驱动 <see cref="ParameterShrinkAnalyzer.TryBuildIndexerPlan"/>，
/// 而它正是 `ForEachScan` 的调用点（`ParameterShrinkAnalyzer.cs:812`，`ForEachScan` 定义在 `:1327`）。
/// </para>
/// <para>
/// <b>为什么关心 `ForEachScan`：</b>它的并行分支调用
/// `runtime.ConcurrencyPool.ForEachAsync(...).GetAwaiter().GetResult()`（`:1357-1370`），
/// 即**在调用线程上同步阻塞**等待旧并发池。若这个调用发生在**内核工作项内部**，
/// 就是 G0-N 要防的「隐藏嵌套」形态。计划附录 A 把它记为「深层调用的真实阻塞只剩 ForEachScan」，
/// 但此前从未运行期验证过。
/// </para>
/// </summary>
public sealed class ParameterShrinkScanNestingTests
{
    /// <summary>
    /// 触发 `ForEachScan` 并行分支：需要**多于一个**语法树
    /// （`ParameterShrinkAnalyzer.cs:1335-1337`：`scans.Count &lt;= 1` 时走串行分支）。
    /// </summary>
    private const string IndexerDeclarationSource = """
        namespace Demo;

        public sealed class Buffer
        {
            public int this[int index, PlayerInput input] => index;
        }
        """;

    /// <summary>第二个语法树，提供索引器访问点，使扫描有意义且 `scans.Count == 2`。</summary>
    private const string IndexerUsageSource = """
        namespace Demo;

        public sealed class Game
        {
            public int Run(Buffer buffer, int frame)
            {
                return buffer[frame, null];
            }
        }
        """;

    private const string PlayerInputSource = """
        namespace Demo;

        public class PlayerInput
        {
        }
        """;

    [Fact]
    public void TryBuildIndexerPlan_WithMultipleSyntaxTrees_TakesParallelScanBranchAndStaysOutsideKernel()
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(PlayerInputSource, path: "player-input.cs"),
            CSharpSyntaxTree.ParseText(IndexerDeclarationSource, path: "buffer.cs"),
            CSharpSyntaxTree.ParseText(IndexerUsageSource, path: "game.cs")
        };

        var options = new RoslynPrototypeExecutionOptions(
          DirectoryMaxDegreeOfParallelism: 2,
          CpgMaxDegreeOfParallelism: 2,
          GroupMaxDegreeOfParallelism: 2,
          HelperMaxDegreeOfParallelism: 2,
          ReplayMaxDegreeOfParallelism: 2,
          MaxConcurrentOperations: 2,
          EnableHelperParallelism: true);
        var workTelemetry = new WorkTelemetryCollector();
        var scheduler = new WorkScheduler(
          AnalysisRuntime.CreateSchedulerOptions(options),
          workTelemetry);
        var pool = new ScanProbeConcurrencyPool(scheduler);
        var runtime = new AnalysisRuntime(
          options,
          new AnalysisEpoch(0, 0, 0),
          concurrencyPool: pool,
          scheduler: scheduler);

        var compilation = CSharpCompilation.Create(
          "scan-probe",
          trees,
          ReferenceAssemblies,
          new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var declarationTree = trees[1];
        var semanticModel = compilation.GetSemanticModel(declarationTree);
        var context = new ScanProbeRuleContext(semanticModel, runtime);

        // 入口约定：传入**索引器参数的 TypeSyntax**（`TryResolveIndexerParameter` 由它向上解析回索引器）。
        // 取第二个参数 `input` 的类型节点 `PlayerInput`：该参数在声明与调用点都被使用，
        // 因而 `TryCollectElementAccessRewrites` 的 `requireCallsites` 才能满足。
        var indexerParameterType = declarationTree.GetRoot()
          .DescendantNodes()
          .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IndexerDeclarationSyntax>()
          .Single()
          .ParameterList
          .Parameters[1]
          .Type!;

        var built = new ParameterShrinkAnalyzer().TryBuildIndexerPlan(
          context,
          indexerParameterType,
          out var plan);

        Assert.True(
          built,
          "索引器计划未构建成功，说明本测试没有真正走到 ForEachScan；"
          + $"设施盘点：{pool.DescribeInventory()}");

        Assert.NotNull(plan);

        // 前置条件：必须真的走了**并行**分支，否则本测试没有触及 :1357 的阻塞调用。
        Assert.True(
          pool.ForEachAsyncMaxDegrees.Count > 0,
          $"未观测到 ForEachAsync 调用，说明走的是串行分支（scans.Count <= 1）；"
          + $"设施盘点：{pool.DescribeInventory()}");

        // ⭐ 本轮核心断言：ForEachScan 的同步阻塞调用**不在**内核工作项内部。
        // 若失败，说明出现了隐藏嵌套（G0-N 要防的形态），消息会点名该设施。
        Assert.True(
          pool.NestedObservations.IsEmpty,
          "ForEachScan 的并行分支在**内核工作项内部**同步阻塞调用旧并发池（隐藏嵌套）: "
          + string.Join("; ", pool.NestedObservations));

        // 反向约束：内核在此期间只应看到**本测试自己的探测提交**（各 MaxConcurrency=2），
        // 不应看到任何由生产代码产生的提交。若将来该路径迁入内核，
        // 会出现与探测提交特征不同的记录，这里会失败并迫使更新结论，而不是悄悄失效。
        var probeSubmissions = workTelemetry.Records.Count;
        var nonProbeSubmissions = workTelemetry.Records
          .Count(record => record.MaxConcurrency != 2);
        Assert.True(
          nonProbeSubmissions == 0,
          $"观测到 {nonProbeSubmissions} 次**非探测**内核提交，说明本路径已进入内核，需更新 G0-N 结论。"
          + $"探测提交 {probeSubmissions} 次；设施盘点：{pool.DescribeInventory()}");

        WriteEvidence(pool, workTelemetry);
    }

    private static readonly MetadataReference[] ReferenceAssemblies = CreateReferences();

    private static MetadataReference[] CreateReferences()
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
          .Split(Path.PathSeparator);
        return trusted
          .Where(path => path.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith("netstandard.dll", StringComparison.OrdinalIgnoreCase))
          .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
          .ToArray();
    }

    private static void WriteEvidence(
      ScanProbeConcurrencyPool pool,
      WorkTelemetryCollector workTelemetry)
    {
        try
        {
            var root = FindRepositoryRoot();
            var path = Path.Combine(
              root,
              "Build",
              "g0m-calibration",
              "foreachscan-nesting.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
              path,
              $"inventory: {pool.DescribeInventory()}{Environment.NewLine}"
              + $"forEachAsyncMaxDegrees: {string.Join(",", pool.ForEachAsyncMaxDegrees)}{Environment.NewLine}"
              + $"nestedObservations: {pool.NestedObservations.Count}{Environment.NewLine}"
              + $"kernelSubmissions: {workTelemetry.Records.Count}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 证据落盘失败不影响契约判定。
        }
    }

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

    /// <summary>最小 `ISemanticRuleContext` 实现，只提供 `ForEachScan` 路径需要的成员。</summary>
    private sealed class ScanProbeRuleContext : ISemanticRuleContext
    {
        public ScanProbeRuleContext(SemanticModel semanticModel, AnalysisRuntime runtime)
        {
            SemanticModel = semanticModel;
            Runtime = runtime;
            SymbolUsageProfile = new SymbolUsageProfile(semanticModel.Compilation);
        }

        public SemanticModel SemanticModel { get; }

        public AnalysisRuntime Runtime { get; }

        public SymbolUsageProfile SymbolUsageProfile { get; }
    }

    /// <summary>装饰旧并发池，记录 `ForEachAsync` 调用并在其回调内探测是否身处内核工作项。</summary>
    private sealed class ScanProbeConcurrencyPool : IConcurrencyPool
    {
        private readonly IConcurrencyPool _inner = new BoundedConcurrencyPool();
        private readonly WorkScheduler _scheduler;
        private readonly ConcurrentDictionary<string, int> _observations = new(StringComparer.Ordinal);

        public ScanProbeConcurrencyPool(WorkScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        public ConcurrentBag<string> NestedObservations { get; } = new();

        public ConcurrentBag<int> ForEachAsyncMaxDegrees { get; } = new();

        public int TotalObservations => _observations.Values.Sum();

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

                _ = probe.Wait(TimeSpan.FromSeconds(10));
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
            ForEachAsyncMaxDegrees.Add(maxDegreeOfParallelism);
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
