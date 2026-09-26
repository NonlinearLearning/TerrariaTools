using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// <summary>
/// 固定「提交深度恒为 1」这一架构决定，防止未来有人重新引入嵌套提交。
/// 这些测试不是为了支持嵌套，而是把当前契约写死：嵌套必然死锁，因此必须
/// 立即、可诊断地失败，而不是偶发挂起。
/// </summary>
public sealed class WorkSchedulerNestingTests
{
    [Fact]
    public async Task RunAsync_WhenExecuteAsyncSubmitsAgain_FailsDiagnosablyInsteadOfDeadlocking()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        var items = new[]
        {
            new WorkItem<int>
            {
                StableOrder = 0,
                ExecuteAsync = async (_, token) =>
                {
                    // 故意在 worker 内再次提交：契约要求可诊断失败，不得死锁。
                    var inner = await scheduler.RunAsync(new WorkSubmission<int>
                    {
                        Items = new[]
                        {
                            new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(7) },
                        },
                    }, token);
                    return inner[0];
                },
            },
        };

        var run = scheduler.RunAsync(new WorkSubmission<int> { Items = items });
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(run, finished);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.Contains("flat", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenCalledFromOutsideAWorkItem_Succeeds()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var first = await scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) } },
        });
        var second = await scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(2) } },
        });

        Assert.Equal(new[] { 1 }, first);
        Assert.Equal(new[] { 2 }, second);
    }

    [Fact]
    public async Task RunAsync_WhenDependentStagesUseDependencies_ProducesTheNestedShapeWithoutNesting()
    {
        // 这是嵌套提交的合规替代形态：分片 → 归并 全部是同一个提交里的节点。
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var items = new[]
        {
            new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) },
            new WorkItem<int> { StableOrder = 1, ExecuteAsync = (_, _) => Task.FromResult(2) },
            new WorkItem<int>
            {
                StableOrder = 2,
                Dependencies = new long[] { 0, 1 },
                ExecuteAsync = (inputs, _) => Task.FromResult(inputs[0] + inputs[1]),
            },
        };

        var results = await scheduler.RunAsync(new WorkSubmission<int> { Items = items });

        Assert.Equal(new[] { 1, 2, 3 }, results);
    }
}
