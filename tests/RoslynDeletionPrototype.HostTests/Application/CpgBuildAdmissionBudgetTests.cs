using MinimalRoslynCpg.Builder;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgBuildAdmissionBudgetTests
{
    [Fact]
    public async Task FairCappedPolicy_GrantsTwoEligibleBuildsAndEventuallyAdmitsLaterFiles()
    {
        var budget = new CpgBuildAdmissionBudget(
          totalDegree: 12,
          CpgBuildAdmissionPolicy.FairCapped);
        using var first = await budget.AcquireAsync(requestedDegree: 12, CancellationToken.None);
        using var second = await budget.AcquireAsync(requestedDegree: 6, CancellationToken.None);
        var laterTwo = budget.AcquireAsync(requestedDegree: 2, CancellationToken.None);
        var laterOne = budget.AcquireAsync(requestedDegree: 1, CancellationToken.None);

        Assert.Equal(6, first.GrantedDegree);
        Assert.Equal(6, second.GrantedDegree);
        Assert.Equal(12, budget.GrantedDegreeInUse);
        Assert.False(laterTwo.IsCompleted);
        Assert.False(laterOne.IsCompleted);

        first.Dispose();
        using var admittedTwo = await laterTwo;
        using var admittedOne = await laterOne;

        Assert.Equal(2, admittedTwo.GrantedDegree);
        Assert.Equal(1, admittedOne.GrantedDegree);
        Assert.Equal(9, budget.GrantedDegreeInUse);
        Assert.Equal(12, budget.GrantedDegreeHighWaterMark);
    }

    [Fact]
    public async Task AcquireAsync_BoundsConcurrentGrantsAndReleasesAfterCancellation()
    {
        var budget = new CpgBuildAdmissionBudget(totalDegree: 4);
        var first = await budget.AcquireAsync(requestedDegree: 3, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancelled = budget.AcquireAsync(requestedDegree: 2, cancellation.Token);

        await Task.Delay(50);
        Assert.False(cancelled.IsCompleted);
        Assert.Equal(3, budget.GrantedDegreeInUse);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
        first.Dispose();

        using var next = await budget.AcquireAsync(requestedDegree: 4, CancellationToken.None);
        Assert.Equal(4, next.GrantedDegree);
        Assert.Equal(4, budget.GrantedDegreeInUse);
        Assert.Equal(4, budget.GrantedDegreeHighWaterMark);
    }
}
