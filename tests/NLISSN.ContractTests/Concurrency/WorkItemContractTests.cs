using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

public sealed class WorkItemContractTests
{
    [Fact]
    public void WorkItem_WhenDependenciesOmitted_DefaultsToEmptyAndThroughput()
    {
        var item = new WorkItem<int>
        {
            StableOrder = 3,
            ExecuteAsync = (_, _) => Task.FromResult(1),
        };

        Assert.Empty(item.Dependencies);
        Assert.Equal(WorkPriority.Throughput, item.Priority);
        Assert.Equal(0, item.EstimatedCost);
        Assert.Equal(0L, item.EstimatedBytes);
    }

    [Fact]
    public void WorkSubmission_WhenMaxConcurrencyOmitted_PreservesOrderByDefault()
    {
        var submission = new WorkSubmission<int>
        {
            Items = new[]
            {
                new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) },
            },
        };

        Assert.True(submission.PreserveOrder);
        Assert.Null(submission.MaxConcurrency);
        Assert.Equal(WorkCategories.Default, submission.Category);
        Assert.Equal(0L, submission.MaxInFlightBytes);
    }

    [Fact]
    public async Task WorkItem_WhenDependencyResultsSupplied_PassesThemToExecuteAsync()
    {
        var dependencyResults = new Dictionary<long, int> { [0] = 41 };

        var item = new WorkItem<int>
        {
            StableOrder = 1,
            Dependencies = new long[] { 0 },
            ExecuteAsync = (results, _) => Task.FromResult(results[0] + 1),
        };

        var produced = await item.ExecuteAsync(dependencyResults, CancellationToken.None);

        Assert.Equal(42, produced);
    }
}
