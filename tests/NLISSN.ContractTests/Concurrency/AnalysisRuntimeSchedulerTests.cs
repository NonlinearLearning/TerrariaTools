using System.Diagnostics;
using NL.Concurrency;
using NLISSN.Core.Pipeline;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// <summary>
/// 固定 AnalysisRuntime 上的内核接线：配额映射、WorkerCount 推导，以及
/// 派生的 runtime 共享同一个内核实例（否则每次 InvalidateCaches 都会泄漏一批 worker）。
/// </summary>
public sealed class AnalysisRuntimeSchedulerTests
{
    private static RoslynPrototypeExecutionOptions CreateOptions(int degree = 6)
    {
        return new RoslynPrototypeExecutionOptions(
          DirectoryMaxDegreeOfParallelism: degree,
          CpgMaxDegreeOfParallelism: degree,
          GroupMaxDegreeOfParallelism: degree,
          HelperMaxDegreeOfParallelism: degree,
          ReplayMaxDegreeOfParallelism: degree,
          MaxConcurrentOperations: degree);
    }

    [Fact]
    public void Scheduler_WhenRuntimeCreated_UsesMaximumCategoryLimitAsWorkerCount()
    {
        var runtime = new AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            DirectoryMaxDegreeOfParallelism: 4,
            CpgMaxDegreeOfParallelism: 12,
            GroupMaxDegreeOfParallelism: 2,
            HelperMaxDegreeOfParallelism: 3,
            ReplayMaxDegreeOfParallelism: 5,
            MaxConcurrentOperations: 6),
          new AnalysisEpoch(0, 0, 0));

        Assert.NotNull(runtime.Scheduler);
        Assert.Equal(12, runtime.Scheduler.WorkerCount);
    }

    [Fact]
    public void Scheduler_WhenParallelismDisabled_MapsFlagIntoCategoryLimit()
    {
        var runtime = new AnalysisRuntime(
          CreateOptions(6) with
          {
              EnableDirectoryParallelism = false,
              EnableGroupParallelism = false,
              EnableHelperParallelism = false,
          },
          new AnalysisEpoch(0, 0, 0));

        var options = runtime.SchedulerOptions;

        Assert.Equal(1, options.ResolveLimit(WorkCategories.Directory));
        Assert.Equal(1, options.ResolveLimit(WorkCategories.RuleGroup));
        Assert.Equal(1, options.ResolveLimit(WorkCategories.Helper));
        Assert.Equal(6, options.ResolveLimit(WorkCategories.Cpg));
        Assert.Equal(6, options.ResolveLimit(WorkCategories.Replay));
        Assert.Equal(6, options.ResolveLimit(WorkCategories.Default));
    }

    [Fact]
    public void Scheduler_WhenParallelismEnabled_KeepsConfiguredLimits()
    {
        var runtime = new AnalysisRuntime(
          CreateOptions(6) with
          {
              EnableDirectoryParallelism = true,
              EnableGroupParallelism = true,
              EnableHelperParallelism = true,
          },
          new AnalysisEpoch(0, 0, 0));

        var options = runtime.SchedulerOptions;

        Assert.Equal(6, options.ResolveLimit(WorkCategories.Directory));
        Assert.Equal(6, options.ResolveLimit(WorkCategories.RuleGroup));
        Assert.Equal(6, options.ResolveLimit(WorkCategories.Helper));
    }

    [Fact]
    public void Scheduler_WhenGroupParallelismLeftAtDefault_IsSerial()
    {
        // EnableGroupParallelism 的默认值是 false，故默认配置下规则组是串行的。
        var options = new RoslynPrototypeExecutionOptions(6, 6, 6, 6, 6, 6);

        var runtime = new AnalysisRuntime(options, new AnalysisEpoch(0, 0, 0));

        Assert.False(options.EnableGroupParallelism);
        Assert.Equal(1, runtime.SchedulerOptions.ResolveLimit(WorkCategories.RuleGroup));
    }

    [Fact]
    public void Scheduler_WhenRuntimeIsDerived_IsSharedNotRecreated()
    {
        // 内核拥有长期 worker；派生 runtime 必须复用同一实例，否则会不断新建线程。
        var runtime = new AnalysisRuntime(CreateOptions(), new AnalysisEpoch(0, 0, 0));

        Assert.Same(runtime.Scheduler, runtime.InvalidateCaches().Scheduler);
        Assert.Same(runtime.Scheduler, runtime.NextEpoch().Scheduler);
    }

    [Fact]
    public void Scheduler_WhenDegreeIsNotPositive_DefaultsToOne()
    {
        var runtime = new AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(0, 0, 0, 0, 0, 0),
          new AnalysisEpoch(0, 0, 0));

        Assert.Equal(1, runtime.Scheduler.WorkerCount);
        Assert.Equal(1, runtime.SchedulerOptions.ResolveLimit(WorkCategories.Cpg));
    }

    [Fact]
    public void SchedulerOptions_WhenSchedulerIsInjected_DescribesThatSchedulerNotExecutionOptions()
    {
        // 构造允许注入 scheduler。若 SchedulerOptions 只是把 ExecutionOptions 再映射一遍，
        // 它就会描述一个**并未在运行**的配额——运行日志据此打印生效值时会撒谎
        // （与已修复的 R4 缺陷同类）。
        var executionOptions = CreateOptions(2);
        var injected = new WorkScheduler(
          new WorkSchedulerOptions(
            DirectoryLimit: 9,
            CpgLimit: 9,
            RuleGroupLimit: 9,
            HelperLimit: 9,
            ReplayLimit: 9,
            MaxConcurrentOperations: 9));

        var runtime = new AnalysisRuntime(
          executionOptions,
          new AnalysisEpoch(0, 0, 0),
          scheduler: injected);

        Assert.Same(injected, runtime.Scheduler);
        // 必须与内核真正使用的配额一致，而不是与 ExecutionOptions 的映射一致。
        Assert.Equal(9, runtime.SchedulerOptions.ResolveLimit(WorkCategories.Cpg));
        Assert.Equal(9, runtime.SchedulerOptions.WorkerCount);
        Assert.Equal(9, runtime.Scheduler.WorkerCount);
    }

    [Fact]
    public async Task WorkTelemetry_WhenSchedulerIsSelfBuilt_ReceivesKernelRecords()
    {
        // 内核若在自建时不接接收端，就会成为遥测黑洞：
        // 提交照常执行，但没有任何记录可用于诊断（运行日志的 pool* 字段也就永远看不到内核）。
        var runtime = new AnalysisRuntime(CreateOptions(2), new AnalysisEpoch(0, 0, 0));

        Assert.NotNull(runtime.WorkTelemetry);

        await runtime.Scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) },
            },
            Category = WorkCategories.RuleGroup,
        });

        var records = runtime.WorkTelemetry!.Records;
        Assert.Single(records);
        Assert.Equal(WorkCategories.RuleGroup, records[0].Category);
        Assert.Equal(1, records[0].InputCount);
    }

    [Fact]
    public void WorkTelemetry_WhenSchedulerIsInjected_IsNotClaimedByRuntime()
    {
        // 注入内核的接收端归注入方所有；本运行时不得冒名声明，
        // 否则会把「别人内核的记录」当作自己的，重复计数。
        var injected = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var runtime = new AnalysisRuntime(
          CreateOptions(2),
          new AnalysisEpoch(0, 0, 0),
          scheduler: injected);

        Assert.Null(runtime.WorkTelemetry);
    }

    [Fact]
    public void WorkTelemetry_WhenRuntimeIsDerived_IsPreservedNotDropped()
    {
        // 派生 runtime 必须复用同一内核，也就要看得见同一个接收端；
        // 丢掉引用会让后续分析阶段的内核记录无人能读。
        var runtime = new AnalysisRuntime(CreateOptions(2), new AnalysisEpoch(0, 0, 0));

        Assert.Same(runtime.WorkTelemetry, runtime.InvalidateCaches().WorkTelemetry);
        Assert.Same(runtime.WorkTelemetry, runtime.NextEpoch().WorkTelemetry);
    }

    [Fact]
    public async Task Scheduler_WhenUsedThroughRuntime_RunsASubmission()
    {
        var runtime = new AnalysisRuntime(CreateOptions(4), new AnalysisEpoch(0, 0, 0));
        var items = Enumerable.Range(0, 5).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            ExecuteAsync = (_, _) => Task.FromResult(index * 2),
        }).ToArray();

        var results = await runtime.Scheduler.RunAsync(
          new WorkSubmission<int> { Items = items, Category = WorkCategories.Cpg });

        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, results);
    }

    // ── G0-L：run 生命周期所有权 ────────────────────────────────────────────────
    // 内核拥有长期 worker，故「谁负责释放」必须可判定：自建者释放、借用者不得释放。

    [Fact]
    public void OwnsScheduler_WhenSchedulerIsSelfBuilt_IsTrue()
    {
        var runtime = new AnalysisRuntime(CreateOptions(2), new AnalysisEpoch(0, 0, 0));

        Assert.True(runtime.OwnsScheduler);
    }

    [Fact]
    public void OwnsScheduler_WhenSchedulerIsInjected_IsFalse()
    {
        // 注入内核归注入方所有；运行时不取得所有权，故不得释放它。
        var runtime = new AnalysisRuntime(
          CreateOptions(2),
          new AnalysisEpoch(0, 0, 0),
          scheduler: new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2)));

        Assert.False(runtime.OwnsScheduler);
    }

    [Fact]
    public void OwnsScheduler_WhenRuntimeIsDerived_IsFalse()
    {
        // 派生 runtime 共享同一内核实例。若它们声称拥有所有权，
        // 释放任意一个派生实例就会关掉原 runtime 仍在使用的内核。
        var runtime = new AnalysisRuntime(CreateOptions(2), new AnalysisEpoch(0, 0, 0));

        Assert.False(runtime.InvalidateCaches().OwnsScheduler);
        Assert.False(runtime.NextEpoch().OwnsScheduler);
    }

    [Fact]
    public async Task DisposeSchedulerAsync_WhenSchedulerIsSelfBuilt_StopsAcceptingSubmissions()
    {
        // 「已释放」的可观察判据：内核不再接受新提交（DisposeAsync 会置 _disposeRequested）。
        var runtime = new AnalysisRuntime(CreateOptions(2), new AnalysisEpoch(0, 0, 0));

        await runtime.DisposeSchedulerAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          runtime.Scheduler.RunAsync(new WorkSubmission<int>
          {
              Items = new[]
              {
                  new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) },
              },
              Category = WorkCategories.Cpg,
          }));
    }

    [Fact]
    public async Task DisposeSchedulerAsync_WhenSchedulerIsInjected_LeavesInjectedSchedulerUsable()
    {
        // 借用方不得提前释放。释放 runtime 后，注入的内核必须仍能正常调度。
        var injected = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        var runtime = new AnalysisRuntime(
          CreateOptions(2),
          new AnalysisEpoch(0, 0, 0),
          scheduler: injected);

        await runtime.DisposeSchedulerAsync();

        var results = await injected.RunAsync(new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(7) },
            },
            Category = WorkCategories.Cpg,
        });

        Assert.Equal(new[] { 7 }, results);
    }

    [Fact]
    public async Task DisposeSchedulerAsync_WhenDerivedRuntimeIsDisposed_KeepsSharedSchedulerUsable()
    {
        // 最关键的回归：派生 runtime 共享内核，释放它不得连带关掉原 runtime 的内核。
        var runtime = new AnalysisRuntime(CreateOptions(2), new AnalysisEpoch(0, 0, 0));
        var derived = runtime.NextEpoch();

        await derived.DisposeSchedulerAsync();

        Assert.Same(runtime.Scheduler, derived.Scheduler);
        var results = await runtime.Scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(11) },
            },
            Category = WorkCategories.Cpg,
        });

        Assert.Equal(new[] { 11 }, results);
    }

    [Fact]
    public async Task DisposeSchedulerAsync_WhenCalledTwice_IsIdempotent()
    {
        // Worker 退出路径与 DisposeAsync 的标记在同一把锁内判定，
        // 故重复释放必须是无害的（成功、失败、取消路径都可能各调一次）。
        var runtime = new AnalysisRuntime(CreateOptions(2), new AnalysisEpoch(0, 0, 0));

        await runtime.DisposeSchedulerAsync();
        await runtime.DisposeSchedulerAsync();

        Assert.True(runtime.OwnsScheduler);
    }

    [Fact]
    public async Task DisposeSchedulerAsync_WhenWorkIsInFlight_DrainsInsteadOfCancelling()
    {
        // G0-L 的「关闭竞争」判据：Dispose 的语义是排空，不是取消。
        // 已接受的在途工作必须跑完，且 Dispose 必须在其结束后返回（不得挂起）。
        var runtime = new AnalysisRuntime(CreateOptions(2), new AnalysisEpoch(0, 0, 0));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;

        var submission = runtime.Scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int>
                {
                    StableOrder = 0,
                    ExecuteAsync = async (_, _) =>
                    {
                        started.TrySetResult();
                        await release.Task;
                        Interlocked.Increment(ref completed);
                        return 1;
                    },
                },
            },
            Category = WorkCategories.Cpg,
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 在途期间发起释放：它必须等待，而不是取消掉这件工作。
        var disposing = runtime.DisposeSchedulerAsync().AsTask();

        // 释放不应在在途工作结束前完成。
        Assert.False(disposing.IsCompleted);

        release.TrySetResult();
        await submission.WaitAsync(TimeSpan.FromSeconds(10));
        await disposing.WaitAsync(TimeSpan.FromSeconds(10));

        // 排空语义：在途工作确实执行完了，没有被 Dispose 取消。
        Assert.Equal(1, Volatile.Read(ref completed));
    }

    /// <summary>
    /// G0-L 缺失的「**重复运行**句柄级证据」：连续多轮「创建 runtime → 提交工作 → 释放」之后，
    /// 线程数不得逐轮累积。
    /// <para>
    /// ⚠️⚠️ <b>必须记录：本用例的线程数断言经实测**没有判别力**，因此它**不是**泄漏证据。</b>
    /// 变异实验（把 <c>WorkScheduler.DisposeAsync</c> 里的 <c>await Task.WhenAll(_workers)</c> 删掉，
    /// 但保留 <c>_disposeRequested</c> 置位）之后，本用例**仍然通过**，且线程读数与干净运行
    /// **完全相同**（<c>33,33,33,33,33</c>）。
    /// </para>
    /// <para>
    /// <b>原因（已读源码确认）：</b><c>WorkScheduler.cs:244</c> 的 worker 循环在
    /// <c>_disposeRequested</c> 置位且无在途提交时**自行 return** ⇒ worker 的终止
    /// **不依赖** <c>DisposeAsync</c> 是否 await 它们；那个 await 只是**同步确认**，不是终止的原因。
    /// 故只要置位发生，线程终将自行退出，线程数断言**因构造而无法判别**。
    /// </para>
    /// <para>
    /// ⇒ 保留本用例只因为「重复运行不累积」这个**上界**描述仍有价值，
    /// 但**不得**把它读作「已验证无泄漏」。真正有判别力的判据见紧随其后的
    /// <see cref="DisposeSchedulerAsync_WhenItReturns_WorkersHaveAlreadyStopped"/>。
    /// </para>
    /// </summary>
    [Fact]
    public async Task DisposeSchedulerAsync_AcrossRepeatedRuns_DoesNotAccumulateWorkers()
    {
        const int rounds = 5;
        var samples = new List<int>();

        for (var round = 0; round < rounds; round++)
        {
            var runtime = new AnalysisRuntime(CreateOptions(4), new AnalysisEpoch(0, 0, 0));

            var results = await runtime.Scheduler.RunAsync(new WorkSubmission<int>
            {
                Items = Enumerable.Range(0, 8)
                  .Select(index => new WorkItem<int>
                  {
                      StableOrder = index,
                      ExecuteAsync = (_, _) => Task.FromResult(index),
                  })
                  .ToArray(),
                Category = WorkCategories.Cpg,
            });

            Assert.Equal(Enumerable.Range(0, 8), results);

            // worker 是本轮唯一需要释放的长期资源。
            await runtime.DisposeSchedulerAsync();

            samples.Add(Process.GetCurrentProcess().Threads.Count);

            // 释放后内核必须拒绝新提交。
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
              runtime.Scheduler.RunAsync(new WorkSubmission<int>
              {
                  Items = new[]
                  {
                      new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) },
                  },
                  Category = WorkCategories.Cpg,
              }));
        }

        // 上界判据：末轮线程数不得比首轮高出「每轮 worker 数 × 轮数」这个量级。
        var baseline = samples[0];
        var final = samples[^1];
        Assert.True(
          final - baseline < rounds * 4,
          $"疑似逐轮累积 worker：轮次线程数={string.Join(",", samples)}；"
          + $"首轮={baseline}、末轮={final}，差值 {final - baseline} 已达「每轮 worker 数 × 轮数」量级。");

        WriteEvidence(samples);
    }

    /// <summary>
    /// G0-L 的**有判别力**的判据：<c>DisposeSchedulerAsync</c> **返回时**，此前接受的
    /// **在途**工作必须已经执行完——即排空发生在**返回之前**，而不是"稍后自己会停"。
    /// <para>
    /// <b>为什么必须在途：</b>本用例最初只提交**已 await 完成**的工作再释放，
    /// 结果变异（删掉 <c>await Task.WhenAll(_workers)</c>）后**仍然通过** ——
    /// 因为那时根本没有在途工作可等，断言无法判别。**已修正为提交在途工作。**
    /// </para>
    /// <para>
    /// 判据：释放返回后立刻检查 —— 在途工作的完成计数必须是**全部**，
    /// 且返回后提交必须被 <see cref="ObjectDisposedException"/> 拒绝。
    /// 删掉那个 await 后，释放会**提前返回**而在途工作尚未跑完 ⇒ 计数不足 ⇒ 失败。
    /// </para>
    /// </summary>
    [Fact]
    public async Task DisposeSchedulerAsync_WhenItReturns_WorkersHaveAlreadyStopped()
    {
        var runtime = new AnalysisRuntime(CreateOptions(4), new AnalysisEpoch(0, 0, 0));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;

        var submission = runtime.Scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = Enumerable.Range(0, 4)
              .Select(index => new WorkItem<int>
              {
                  StableOrder = index,
                  ExecuteAsync = async (_, _) =>
                  {
                      started.TrySetResult();
                      await release.Task;
                      Interlocked.Increment(ref completed);
                      return index;
                  },
              })
              .ToArray(),
            Category = WorkCategories.Cpg,
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var disposing = runtime.DisposeSchedulerAsync().AsTask();

        // 释放必须**等待**在途工作，而不是提前返回。
        // （无 await 的变异实现会让这里立即完成 ⇒ 本断言失败。）
        Assert.False(disposing.IsCompleted);

        release.TrySetResult();
        await submission.WaitAsync(TimeSpan.FromSeconds(10));
        await disposing.WaitAsync(TimeSpan.FromSeconds(10));

        // 返回时全部在途工作必须已执行完：排空发生在返回**之前**。
        Assert.Equal(4, Volatile.Read(ref completed));

        // 且返回后内核已拒绝新提交。
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          runtime.Scheduler.RunAsync(new WorkSubmission<int>
          {
              Items = new[]
              {
                  new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) },
              },
              Category = WorkCategories.Cpg,
          }));
    }

    private static void WriteEvidence(IReadOnlyList<int> samples)
    {
        try
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null &&
                   !(File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                     Directory.Exists(Path.Combine(directory.FullName, "src"))))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                return;
            }

            var path = Path.Combine(
              directory.FullName,
              "Build",
              "g0m-calibration",
              "repeated-run-workers.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
              path,
              $"rounds: {samples.Count}{Environment.NewLine}"
              + $"threadCountPerRound: {string.Join(",", samples)}{Environment.NewLine}"
              + $"delta(first,last): {samples[^1] - samples[0]}{Environment.NewLine}"
              + $"upperBound(rounds*4): {samples.Count * 4}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 证据落盘失败不影响契约判定。
        }
    }
}
