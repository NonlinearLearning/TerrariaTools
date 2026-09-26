using NL.Concurrency;
using NLISSN.Application;
using RoslynPrototype.Tests.TestCodeSet.Target;
using Xunit;

namespace RoslynPrototype.Tests.Application;

/// 固定 S2-3 的结构性前置条件：目录层并行**不能**直接包在内核 RunAsync 里。
/// 每个文件的 `AnalyzeFile` 内部都会经由规则图向同一内核提交，
/// 因此把目录层循环再包一层 `Scheduler.RunAsync` 会触发内核的嵌套拒绝（设计 §10.3）。
/// 本测试证明“文件级分析确实已经在向内核提交”，从而该嵌套风险是真实存在的而非推测。
public sealed class DirectorySchedulerBoundaryTests
{
  [Fact]
  public void Analyze_WithSchedulerBackedRuntime_PerFileAnalysisSubmitsToKernel()
  {
    var sources = new[]
    {
      new DirectorySourceFile(0, "a.cs", AtomicExpressionSources.AtomicNameSource),
      new DirectorySourceFile(1, "b.cs", AtomicExpressionSources.AtomicNameSource)
    };
    var options = new RoslynPrototypeExecutionOptions(
      DirectoryMaxDegreeOfParallelism: 2,
      CpgMaxDegreeOfParallelism: 2,
      GroupMaxDegreeOfParallelism: 2,
      HelperMaxDegreeOfParallelism: 2,
      ReplayMaxDegreeOfParallelism: 2,
      MaxConcurrentOperations: 2,
      EnableDirectoryParallelism: true,
      EnableGroupParallelism: true);
    var telemetry = new WorkTelemetryCollector();
    var runtime = new AnalysisRuntime(
      options,
      new AnalysisEpoch(0, 0, 0),
      scheduler: new WorkScheduler(
        AnalysisRuntime.CreateSchedulerOptions(options),
        telemetry));

    var outcome = new DirectoryAnalysisUseCase(RulePipelineTestFactory.Create()).Analyze(
      sources,
      AnalysisLegacyOptionsTestExtensions.CreateSettings(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
          ["target-name"] = "s"
        }),
      runtime);

    Assert.Equal(new[] { 0, 1 }, outcome.FileResults.Select(file => file.Index));

    // 关键事实：规则图（RuleGroup）提交来自文件级分析内部。
    // 若把目录层循环也包成一次 RunAsync，这些提交就变成嵌套提交。
    var ruleGroupSubmissions = telemetry.Records
      .Where(record => record.Category == WorkCategories.RuleGroup)
      .ToList();
    Assert.True(
      ruleGroupSubmissions.Count >= sources.Length,
      $"期望每个文件至少产生一次规则图提交，实际 {ruleGroupSubmissions.Count} 次（文件 {sources.Length} 个）");
  }

  /// 本测试直接执行 S2-3 计划中的形态：把目录级分析整体放进一个内核工作项，
  /// 并断言它会被嵌套守卫拒绝。这就是“S2-3 不能按现计划落地”的实证。
  [Fact]
  public void Analyze_InvokedInsideKernelWorkItem_IsRejectedAsNestedSubmission()
  {
    var sources = new[]
    {
      new DirectorySourceFile(0, "a.cs", AtomicExpressionSources.AtomicNameSource)
    };
    var options = new RoslynPrototypeExecutionOptions(
      DirectoryMaxDegreeOfParallelism: 2,
      CpgMaxDegreeOfParallelism: 2,
      GroupMaxDegreeOfParallelism: 2,
      HelperMaxDegreeOfParallelism: 2,
      ReplayMaxDegreeOfParallelism: 2,
      MaxConcurrentOperations: 2,
      EnableDirectoryParallelism: true,
      EnableGroupParallelism: true);
    var scheduler = new WorkScheduler(
      AnalysisRuntime.CreateSchedulerOptions(options));
    var runtime = new AnalysisRuntime(
      options,
      new AnalysisEpoch(0, 0, 0),
      scheduler: scheduler);
    var useCase = new DirectoryAnalysisUseCase(RulePipelineTestFactory.Create());
    var settings = AnalysisLegacyOptionsTestExtensions.CreateSettings(
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["target-name"] = "s"
      });

    Exception? observed = null;
    scheduler.RunAsync(
      new WorkSubmission<int>
      {
        Items = new[]
        {
          new WorkItem<int>
          {
            StableOrder = 0,
            ExecuteAsync = (_, _) =>
            {
              try
              {
                useCase.Analyze(sources, settings, runtime);
              }
              catch (Exception exception)
              {
                observed = exception;
              }

              return Task.FromResult(0);
            },
          },
        },
      }).GetAwaiter().GetResult();
    scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();

    var error = Assert.IsType<InvalidOperationException>(observed);
    Assert.Contains("flat", error.Message, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// 上一条测试只传了**一个**源文件，因此 <c>DirectoryAnalysisUseCase</c> 会在
  /// 「源数量 &lt;= 1」处分流到**串行**分支，`SelectOrderedAsync` 的并行目录路径
  /// （<c>DirectoryAnalysisUseCase.cs:267-285</c>）**从未被执行**。
  /// 本测试用多个源文件 + 目录并行度 &gt; 1 真正走进并行分支，再断言它同样被嵌套守卫拒绝。
  /// <para>
  /// 该形态比串行分支更值得固定：并行分支经由 <c>runtime.ConcurrencyPool.SelectOrderedAsync</c>
  /// 在**线程池线程**上执行每个文件的 <c>AnalyzeFile</c>，而每文件内部又向同一个内核提交规则图。
  /// 若该池内部压制了 <c>ExecutionContext</c> 流，<c>AsyncLocal</c> 守卫就会失效——
  /// 那将是**真实的生产缺口**（参见轮次 10 记录的 <c>SuppressFlow</c> 边界）。
  /// 因此本测试同时是对该风险的运行期探测：绿=守卫生效，红=并行目录路径可绕过守卫。
  /// </para>
  /// </summary>
  [Fact]
  public async Task Analyze_WithDirectoryParallelismInsideKernelWorkItem_IsRejectedAsNestedSubmission()
  {
    // 必须 >1 个源，否则会走串行分支，本测试就失去意义。
    var sources = new[]
    {
      new DirectorySourceFile(0, "a.cs", AtomicExpressionSources.AtomicNameSource),
      new DirectorySourceFile(1, "b.cs", AtomicExpressionSources.AtomicNameSource),
      new DirectorySourceFile(2, "c.cs", AtomicExpressionSources.AtomicNameSource)
    };
    var options = new RoslynPrototypeExecutionOptions(
      DirectoryMaxDegreeOfParallelism: 3,
      CpgMaxDegreeOfParallelism: 2,
      GroupMaxDegreeOfParallelism: 2,
      HelperMaxDegreeOfParallelism: 2,
      ReplayMaxDegreeOfParallelism: 2,
      MaxConcurrentOperations: 2,
      EnableDirectoryParallelism: true,
      EnableGroupParallelism: true);
    var scheduler = new WorkScheduler(
      AnalysisRuntime.CreateSchedulerOptions(options));
    var runtime = new AnalysisRuntime(
      options,
      new AnalysisEpoch(0, 0, 0),
      scheduler: scheduler);
    var useCase = new DirectoryAnalysisUseCase(RulePipelineTestFactory.Create());
    var settings = AnalysisLegacyOptionsTestExtensions.CreateSettings(
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["target-name"] = "s"
      });

    Exception? observed = null;
    var run = scheduler.RunAsync(
      new WorkSubmission<int>
      {
        Items = new[]
        {
          new WorkItem<int>
          {
            StableOrder = 0,
            ExecuteAsync = (_, _) =>
            {
              try
              {
                useCase.Analyze(sources, settings, runtime);
              }
              catch (Exception exception)
              {
                observed = exception;
              }

              return Task.FromResult(0);
            },
          },
        },
      });

    // 超时保护：若守卫在并行分支上失效，内层提交可能排队等待，不能让套件无限挂起。
    var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)));
    Assert.True(
      ReferenceEquals(finished, run),
      "目录并行分析在内核工作项内既未抛错也未在 30 秒内结束——并行分支可能绕过了嵌套守卫。");
    await run;
    await scheduler.DisposeAsync();

    var error = Assert.IsType<InvalidOperationException>(observed);
    Assert.Contains("flat", error.Message, StringComparison.OrdinalIgnoreCase);
  }
}
