using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// <summary>
/// G0-N：计划 :118 与「跨实例嵌套」并列禁止的绕开方式——<c>Task.Run</c> 包装、
/// **同步等待**、以及 <c>ExecutionContext.SuppressFlow()</c>——究竟哪些被守卫覆盖。
/// <para>
/// 背景：轮次 9 曾把「<c>Task.Run</c> 包装与同步等待未加守卫也无测试」写入计划附录 H 与
/// feature_list。那是**阅读推断，不是实测**。用最小隔离探针实测后证明该说法**错误**：
/// 守卫读取 <see cref="AsyncLocal{T}"/>，而 <c>AsyncLocal</c> 默认随
/// <c>ExecutionContext</c> **流入** <c>Task.Run</c>，同步等待更是在**同一线程**上调用，
/// 故两者**本来就被拦住**。本文件把实测事实固定下来。
/// </para>
/// <para>
/// 实测同时暴露了一个**真实残余缺口**：<c>ExecutionContext.SuppressFlow()</c> 会阻止
/// <c>AsyncLocal</c> 流入新线程，此时守卫读到 null 而放行。该缺口**无法**用「全局计数」之类
/// 的手段修补，因为那会误伤「两个互不嵌套的内核合法并行」这一被本套件明确保护的形态
/// （见 <c>WorkSchedulerCrossInstanceNestingTests</c> 的反例保护用例）。
/// 生产侧 <c>src/</c> 中 <c>SuppressFlow</c> **零命中**，故这是**记录在案的边界**而非生产风险。
/// </para>
/// <para>
/// 所有用例都带超时：这些形态若未被拦截本会死锁，没有超时的测试会挂住整个套件。
/// </para>
/// </summary>
public sealed class WorkSchedulerNestingBypassTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RunAsync_WhenWorkItemWrapsSubmissionInTaskRun_IsRejected()
    {
        // AsyncLocal 随 ExecutionContext 流入 Task.Run，故这条**已被守卫覆盖**。
        // 固定该事实：若将来有人改成 thread-static 或 SuppressFlow 包裹，会在此失败。
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var run = scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int>
                {
                    StableOrder = 0,
                    ExecuteAsync = async (_, token) =>
                    {
                        return await Task.Run(
                          async () => (await scheduler.RunAsync(
                            new WorkSubmission<int>
                            {
                                Items = new[]
                                {
                                    new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(5) },
                                },
                            },
                            token))[0]);
                    },
                },
            },
        });

        var finished = await Task.WhenAny(run, Task.Delay(Timeout));
        Assert.Same(run, finished);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.Contains("flat", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenWorkItemBlocksSynchronouslyOnSubmission_IsRejected()
    {
        // 同步等待形态：守卫在**调用点**即抛出，故不会真的阻塞。
        // 若未被拦截，本用例会因死锁而超时失败（Assert.Same 不成立）。
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var run = scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int>
                {
                    StableOrder = 0,
                    ExecuteAsync = (_, _) =>
                    {
                        var inner = scheduler.RunAsync(new WorkSubmission<int>
                        {
                            Items = new[]
                            {
                                new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(5) },
                            },
                        });
                        return Task.FromResult(inner.GetAwaiter().GetResult()[0]);
                    },
                },
            },
        });

        var finished = await Task.WhenAny(run, Task.Delay(Timeout));
        Assert.Same(run, finished);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run);
    }

    [Fact]
    public async Task RunAsync_WhenFlowIsSuppressed_PinsTheKnownResidualGap()
    {
        // ⚠️ 本用例**固定的是一个已知缺口**，不是期望的行为契约。
        // SuppressFlow 阻止 AsyncLocal 流入新线程 -> 守卫读到 null -> 内层提交被放行。
        // 断言方向刻意取「缺口确实存在」：这样一旦有人（有意或无意）堵上该缺口，
        // 本用例会失败并**强迫**他更新这处记录，而不是让边界悄悄漂移。
        //
        // 该缺口不可用「全局在途计数」之类手段修补：那会误伤两个互不嵌套的内核
        // 合法并行的形态（WorkSchedulerCrossInstanceNestingTests 有反例保护用例）。
        // 生产 src/ 中 SuppressFlow 零命中，故仅作边界记录。
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var innerRan = false;

        var run = scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int>
                {
                    StableOrder = 0,
                    ExecuteAsync = async (_, token) =>
                    {
                        // ⚠️ SuppressFlow 的作用域**不得跨 await**：AsyncFlowControl.Undo()
                        // 是线程亲和的，跨 await 会抛
                        // "AsyncFlowControl objects can be used to restore flow only on a
                        //  Context that had its flow suppressed."
                        // 只需在 Task.Run **开始**的那一刻压制流即可，故先拿到 Task，再 await。
                        Task<int> innerTask;
                        using (ExecutionContext.SuppressFlow())
                        {
                            innerTask = Task.Run(
                              async () => (await scheduler.RunAsync(
                                new WorkSubmission<int>
                                {
                                    Items = new[]
                                    {
                                        new WorkItem<int>
                                        {
                                            StableOrder = 0,
                                            ExecuteAsync = (_, _) =>
                                            {
                                                innerRan = true;
                                                return Task.FromResult(9);
                                            },
                                        },
                                    },
                                },
                                token))[0]);
                        }

                        return await innerTask;
                    },
                },
            },
        });

        var finished = await Task.WhenAny(run, Task.Delay(Timeout));

        // 关键：即便守卫被绕过，也**不得死锁**——内层有独立 worker，能自行完成。
        Assert.Same(run, finished);
        var results = await run;
        Assert.Equal(new[] { 9 }, results);

        // 固定缺口：内层真的执行了，守卫未拦截。
        Assert.True(
          innerRan,
          "KNOWN GAP no longer reproduces: the guard now catches SuppressFlow-wrapped submissions. "
          + "Update plan appendix H and the feature_list boundary note accordingly.");
    }

    [Fact]
    public async Task RunAsync_WhenAnotherSchedulerHasAnItemInFlight_IsNotSpuriouslyRejected()
    {
        // ⚠️ 本用例是**决定性的反例**，用于证明 SuppressFlow 缺口**不能**用
        // 「全局在途计数」修补（若那样修，本用例会失败）。它刻意**不依赖竞态**：
        //   1. 内核 A 的工作项被显式闸门**挂住**，保证计数长期 > 0；
        //   2. 此时在**顶层**（不在任何工作项内）向内核 B 提交。
        // 正确语义：B 必须成功——它与 A 互不嵌套。
        // 若守卫改成读全局在途计数，B 会被**误判**为嵌套而拒绝。
        await using var first = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        await using var second = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRun = first.RunAsync(new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int>
                {
                    StableOrder = 0,
                    // 先发"已开始"信号，再一直挂住，直到我们放行。
                    ExecuteAsync = async (_, _) =>
                    {
                        started.TrySetResult(0);
                        return await gate.Task;
                    },
                },
            },
        });

        try
        {
            // 确定性地等到 A 的工作项**真正开始执行**——此时 A 必有在途项。
            // 不依赖 sleep 或竞态：信号由工作项自己发出。
            var startedOrTimeout = await Task.WhenAny(started.Task, Task.Delay(Timeout));
            Assert.Same(started.Task, startedOrTimeout);

            // 现在 A 有在途项。从**顶层**提交 B —— 必须成功。
            var secondRun = second.RunAsync(new WorkSubmission<int>
            {
                Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(2) } },
            });

            var finished = await Task.WhenAny(secondRun, Task.Delay(Timeout));
            Assert.Same(secondRun, finished);
            Assert.Equal(new[] { 2 }, await secondRun);
        }
        finally
        {
            gate.TrySetResult(0);
            var drain = await Task.WhenAny(firstRun, Task.Delay(Timeout));
            Assert.Same(firstRun, drain);
            await firstRun;
        }
    }
}
