using MinimalRoslynCpg.Builder;
using Xunit;

namespace RoslynPrototype.PerformanceTests.Concurrency;

public sealed class CpgBuildAdmissionBudgetTests
{
  [Fact]
  public async Task AcquireAsync_NeverGrantsMoreThanConfiguredTotal()
  {
    var budget = new CpgBuildAdmissionBudget(totalDegree: 4);
    using var first = await budget.AcquireAsync(requestedDegree: 3, CancellationToken.None);
    var secondTask = budget.AcquireAsync(requestedDegree: 2, CancellationToken.None);

    await Task.Delay(50);

    Assert.False(secondTask.IsCompleted);
    Assert.Equal(3, budget.GrantedDegreeInUse);
    Assert.Equal(3, budget.GrantedDegreeHighWaterMark);

    first.Dispose();
    using var second = await secondTask;

    Assert.Equal(2, second.GrantedDegree);
    Assert.Equal(2, budget.GrantedDegreeInUse);
    Assert.Equal(3, budget.GrantedDegreeHighWaterMark);
  }

  [Fact]
  public async Task AcquireAsync_CancellationAndDisposalReleaseAllLeases()
  {
    var budget = new CpgBuildAdmissionBudget(totalDegree: 2);
    using var first = await budget.AcquireAsync(requestedDegree: 2, CancellationToken.None);
    using var cancellation = new CancellationTokenSource();
    var waiting = budget.AcquireAsync(requestedDegree: 1, cancellation.Token);

    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
    Assert.Equal(2, budget.GrantedDegreeInUse);

    first.Dispose();
    using var next = await budget.AcquireAsync(requestedDegree: 2, CancellationToken.None);

    Assert.Equal(1, budget.ActiveLeaseCount);
    Assert.Equal(2, next.GrantedDegree);
  }
}
