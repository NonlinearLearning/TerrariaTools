using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// <summary>
/// G0-N「提交深度恒为 1」的**跨实例**半边。
/// <para>
/// 既有 <c>WorkSchedulerNestingTests</c> 固定了「同一实例在 worker 内再次提交」必须可诊断失败。
/// 但生产守卫是 <c>ReferenceEquals(CurrentScheduler.Value, this)</c>（<c>WorkScheduler.cs:76</c>），
/// 只识别**同一个**实例。计划 :118 明确禁止另一种绕开方式：
/// 「禁止 worker 内创建第二个 scheduler、Task.Run 包装或同步等待来绕开同实例嵌套守卫」。
/// </para>
/// <para>
/// 该形态若不被拦截，后果正是 G0-N 要防的「统一 P 不成立」：
/// 内层 scheduler 自带一份 P 个长期 worker，两者相加构成 P₁+P₂；
/// 若外层 worker 全被该同步等待占满，还会退化为死锁。
/// </para>
/// </summary>
public sealed class WorkSchedulerCrossInstanceNestingTests
{
    [Fact]
    public async Task RunAsync_WhenAWorkItemCreatesASecondScheduler_FailsDiagnosablyInsteadOfMultiplyingP()
    {
        await using var outer = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        WorkScheduler? inner = null;
        Exception? observed = null;

        try
        {
            var run = outer.RunAsync(new WorkSubmission<int>
            {
                Items = new[]
                {
                    new WorkItem<int>
                    {
                        StableOrder = 0,
                        ExecuteAsync = async (_, token) =>
                        {
                            // 计划 :118 禁止的形态：worker 内**新建**一个 scheduler 来补并行。
                            inner = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
                            try
                            {
                                var results = await inner.RunAsync(
                                  new WorkSubmission<int>
                                  {
                                      Items = new[]
                                      {
                                          new WorkItem<int>
                                          {
                                              StableOrder = 0,
                                              ExecuteAsync = (_, _) => Task.FromResult(11),
                                          },
                                      },
                                  },
                                  token);
                                return results[0];
                            }
                            catch (Exception exception)
                            {
                                observed = exception;
                                throw;
                            }
                        },
                    },
                },
            });

            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));

            // 契约：必须**立即、可诊断地**失败，而不是悄悄多起一份 worker 或偶发挂起。
            Assert.Same(run, finished);
            await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        }
        finally
        {
            if (inner is not null)
            {
                await inner.DisposeAsync();
            }
        }

        Assert.NotNull(observed);
        Assert.IsType<InvalidOperationException>(observed);
    }

    [Fact]
    public async Task RunAsync_WhenASecondSchedulerIsUsedOutsideAnyWorkItem_StillSucceeds()
    {
        // 反例保护：两个**互不嵌套**的内核必须都能正常工作。
        // 守卫只应拦截「在别人的工作项里」这一种形态，不得变成一个全局单例限制。
        await using var first = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        await using var second = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var firstResults = await first.RunAsync(new WorkSubmission<int>
        {
            Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) } },
        });
        var secondResults = await second.RunAsync(new WorkSubmission<int>
        {
            Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(2) } },
        });

        Assert.Equal(new[] { 1 }, firstResults);
        Assert.Equal(new[] { 2 }, secondResults);
    }

    [Fact]
    public async Task RunAsync_WhenASecondSchedulerIsUsedSequentiallyAfterTheFirstCompleted_Succeeds()
    {
        // 顺序使用（非嵌套）也必须放行：守卫判定的是**当前是否在别人的工作项内**，
        // 不是「本进程是否已有过另一个内核」。
        await using var first = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        var firstResults = await first.RunAsync(new WorkSubmission<int>
        {
            Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) } },
        });

        await using var second = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        var secondResults = await second.RunAsync(new WorkSubmission<int>
        {
            Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(2) } },
        });

        Assert.Equal(new[] { 1 }, firstResults);
        Assert.Equal(new[] { 2 }, secondResults);
    }
}
