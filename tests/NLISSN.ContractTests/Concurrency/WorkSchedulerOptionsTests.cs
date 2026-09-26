using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

public sealed class WorkSchedulerOptionsTests
{
    [Fact]
    public void WorkerCount_WhenCategoryLimitsDiffer_EqualsMaximum()
    {
        var options = new WorkSchedulerOptions(
            DirectoryLimit: 4,
            CpgLimit: 12,
            RuleGroupLimit: 4,
            HelperLimit: 4,
            ReplayLimit: 4,
            MaxConcurrentOperations: 8);

        Assert.Equal(12, options.WorkerCount);
    }

    [Fact]
    public void ResolveLimit_ForUnknownCategory_Throws()
    {
        var options = new WorkSchedulerOptions(1, 1, 1, 1, 1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => options.ResolveLimit("nope"));
    }

    [Fact]
    public void ResolveLimit_WhenParallelismDisabled_ReturnsOne()
    {
        var options = new WorkSchedulerOptions(1, 1, 1, 1, 1, 1) with
        {
            DirectoryParallelism = false,
        };

        Assert.Equal(1, options.ResolveLimit(WorkCategories.Directory));
    }

    [Fact]
    public void ResolveLimit_WhenGroupParallelismDisabled_ReturnsOne()
    {
        var options = new WorkSchedulerOptions(1, 1, 8, 1, 1, 1);

        Assert.Equal(1, options.ResolveLimit(WorkCategories.RuleGroup));
    }

    [Fact]
    public void ResolveLimit_WhenGroupParallelismEnabled_ReturnsConfiguredLimit()
    {
        var options = new WorkSchedulerOptions(1, 1, 8, 1, 1, 1) with
        {
            GroupParallelism = true,
        };

        Assert.Equal(8, options.ResolveLimit(WorkCategories.RuleGroup));
    }

    [Fact]
    public void WorkerCount_WhenAllLimitsBelowOne_NeverDropsBelowOne()
    {
        var options = new WorkSchedulerOptions(0, 0, 0, 0, 0, 0);

        Assert.Equal(1, options.WorkerCount);
    }
}
