using System.Collections.Concurrent;
using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.PerformanceTests.Concurrency;

public sealed class ConcurrencyPoolContractTests
{
    [Fact]
    public async Task AdmissionController_WhenBothClassesWait_UsesWeightedTurnsBeforeThroughputAdmission()
    {
        var controller = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 4,
          MaxReservedByteCount: 1024,
          LatencySensitiveWeight: 3,
          ThroughputWeight: 1));
        using var firstLease = await controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.LatencySensitive));
        var latencyOne = controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.LatencySensitive));
        var latencyTwo = controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.LatencySensitive));
        var throughput = controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.Throughput));

        firstLease.Dispose();
        using var latencyOneLease = await latencyOne.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(throughput.IsCompleted);

        latencyOneLease.Dispose();
        using var latencyTwoLease = await latencyTwo.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(throughput.IsCompleted);

        latencyTwoLease.Dispose();
        using var throughputLease = await throughput.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ConcurrencyAdmissionReason.WeightedTurn, throughputLease.AdmissionReason);
    }

    [Fact]
    public async Task AdmissionController_WhenLatencyQueueIsEmpty_DoesNotSpendItsTurn()
    {
        var controller = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 2,
          MaxReservedByteCount: 1024));

        using var throughputLease = await controller.AcquireAsync(
          new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.Throughput));

        Assert.Equal(ConcurrencyAdmissionReason.WeightedTurn, throughputLease.AdmissionReason);
    }

    [Fact]
    public async Task AdmissionController_WhenThroughputWaitsPastTheLimit_AdmitsItBeforeAnotherLatencyTurn()
    {
        var controller = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 4,
          MaxReservedByteCount: 1024,
          LatencySensitiveWeight: 10,
          ThroughputWeight: 1,
          MaximumThroughputWait: TimeSpan.FromMilliseconds(20)));
        using var firstLease = await controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.LatencySensitive));
        var throughput = controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.Throughput));
        var latency = controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.LatencySensitive));

        await Task.Delay(TimeSpan.FromMilliseconds(50));
        firstLease.Dispose();

        using var throughputLease = await throughput.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ConcurrencyAdmissionReason.AgingPromotion, throughputLease.AdmissionReason);
        throughputLease.Dispose();
        using var latencyLease = await latency.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AdmissionController_WhenReservationExceedsConfiguredBudget_FailsBeforeEnqueueing()
    {
        var controller = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 1,
          MaxReservedByteCount: 128));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => controller.AcquireAsync(
          new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.Throughput, ReservedByteCount: 129)));
    }

    [Fact]
    public async Task AdmissionController_WhenWaiterIsCanceled_ReleasesTheQueueForTheNextWaiter()
    {
        var controller = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 1,
          MaxReservedByteCount: 128));
        using var firstLease = await controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.LatencySensitive));
        using var cancellation = new CancellationTokenSource();
        var canceledWaiter = controller.AcquireAsync(
          new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.Throughput),
          cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledWaiter);

        var nextWaiter = controller.AcquireAsync(new ConcurrencyAdmissionRequest(ConcurrencyWorkClass.Throughput));
        firstLease.Dispose();
        using var nextLease = await nextWaiter.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SelectOrderedAsync_WhenWorkCompletesOutOfOrder_ReturnsInputOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var sources = new[] { 0, 1, 2 };

        var results = await pool.SelectOrderedAsync(
          sources,
          maxDegreeOfParallelism: 3,
          async (source, _, _) =>
          {
              await Task.Delay((2 - source) * 10);
              return source;
          });

        Assert.Equal(sources, results);
    }

    [Fact]
    public async Task SelectOrderedAsync_WhenAdmissionIsOccupied_DoesNotStartWorkUntilTheLeaseIsReleased()
    {
        var admissionController = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 2,
          MaxReservedByteCount: 0));
        var pool = new BoundedConcurrencyPool(telemetrySink: null, admissionController: admissionController);
        var firstWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstOperation = pool.SelectOrderedAsync(
          new[] { 0 },
          maxDegreeOfParallelism: 1,
          async (_, _, _) =>
          {
              firstWorkStarted.TrySetResult();
              await releaseFirstWork.Task;
              return 0;
          });
        await firstWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondOperation = pool.SelectOrderedAsync(
          new[] { 1 },
          maxDegreeOfParallelism: 1,
          (_, _, _) =>
          {
              secondWorkStarted.TrySetResult();
              return Task.FromResult(1);
          });

        Assert.NotSame(
          secondWorkStarted.Task,
          await Task.WhenAny(secondWorkStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(150))));

        releaseFirstWork.TrySetResult();
        await Task.WhenAll(firstOperation, secondOperation).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(secondWorkStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task SelectCpuBoundOrdered_WhenWorkCompletesOutOfOrder_ReturnsInputOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var active = 0;
        var peak = 0;

        var results = await pool.SelectCpuBoundOrdered(
          new[] { 0, 1, 2 },
          maxDegreeOfParallelism: 3,
          (source, _, _) =>
          {
              var current = Interlocked.Increment(ref active);
              InterlockedExtensions.Max(ref peak, current);
              try
              {
                  Thread.Sleep((2 - source) * 20);
                  return source;
              }
              finally
              {
                  Interlocked.Decrement(ref active);
              }
          });

        Assert.Equal(new[] { 0, 1, 2 }, results);
        Assert.InRange(peak, 1, 3);
    }

    [Fact]
    public async Task SelectCpuBoundOrdered_WhenOperationCompletes_ReportsBoundedTelemetry()
    {
        var telemetrySink = new RecordingTelemetrySink();
        var pool = new BoundedConcurrencyPool(telemetrySink);

        var results = await pool.SelectCpuBoundOrdered(
          new[] { 1, 2, 3 },
          maxDegreeOfParallelism: 2,
          (source, _, _) => source * 2);

        var telemetry = Assert.Single(telemetrySink.Operations);
        Assert.Equal(ConcurrencyOperationKind.CpuBoundOrderedSelection, telemetry.OperationKind);
        Assert.Equal(3, telemetry.SourceCount);
        Assert.Equal(2, telemetry.RequestedMaxDegreeOfParallelism);
        Assert.InRange(telemetry.PeakActiveWorkItemCount, 1, 2);
        Assert.Equal(0, telemetry.PeakCompletedBufferItemCount);
        Assert.False(telemetry.WasCanceled);
        Assert.Equal(new[] { 2, 4, 6 }, results);
    }

    [Fact]
    public async Task ForEachAsync_WhenParallelismIsBounded_DoesNotExceedRequestedDegree()
    {
        var pool = new BoundedConcurrencyPool();
        var active = 0;
        var peak = 0;
        var firstWindowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWindow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = pool.ForEachAsync(
          new[] { 0, 1, 2, 3 },
          maxDegreeOfParallelism: 2,
          async (_, _, _) =>
          {
              var current = Interlocked.Increment(ref active);
              InterlockedExtensions.Max(ref peak, current);
              if (current == 2)
              {
                  firstWindowStarted.TrySetResult();
              }

              try
              {
                  await releaseFirstWindow.Task;
              }
              finally
              {
                  Interlocked.Decrement(ref active);
              }
          });

        await firstWindowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFirstWindow.TrySetResult();
        await runTask;
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task ForEachAsync_WhenCancellationIsRequested_DoesNotStartQueuedWork()
    {
        var pool = new BoundedConcurrencyPool();
        using var cancellation = new CancellationTokenSource();
        var started = new ConcurrentQueue<int>();
        var firstWindowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = pool.ForEachAsync(
          new[] { 0, 1, 2, 3, 4 },
          maxDegreeOfParallelism: 2,
          async (source, _, token) =>
          {
              started.Enqueue(source);
              if (started.Count == 2)
              {
                  firstWindowStarted.TrySetResult();
              }

              await Task.Delay(Timeout.InfiniteTimeSpan, token);
          },
          cancellation.Token);

        await firstWindowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation);
        Assert.DoesNotContain(started, source => source >= 2);
    }

    [Fact]
    public async Task SelectOrderedAsync_WhenWorkItemBlocksBeforeItsFirstAwait_StartsTheRestOfTheWorkerWindow()
    {
        var pool = new BoundedConcurrencyPool();
        var firstWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runTask = pool.SelectOrderedAsync(
          new[] { 0, 1 },
          maxDegreeOfParallelism: 2,
          async (source, _, _) =>
          {
              if (source == 0)
              {
                  firstWorkStarted.TrySetResult();
                  releaseFirstWork.Task.GetAwaiter().GetResult();
              }
              else
              {
                  secondWorkStarted.TrySetResult();
              }

              await Task.Yield();
              return source;
          });

        try
        {
            await firstWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await secondWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            releaseFirstWork.TrySetResult();
            await runTask;
        }
    }

    [Fact]
    public async Task SelectOrderedAsync_WhenWorkItemFails_DoesNotStartQueuedWorkAfterTheFailure()
    {
        var pool = new BoundedConcurrencyPool();
        var started = new ConcurrentQueue<int>();
        var secondWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = pool.SelectOrderedAsync(
          new[] { 0, 1, 2, 3, 4 },
          maxDegreeOfParallelism: 2,
          async (source, _, cancellationToken) =>
          {
              started.Enqueue(source);
              if (source == 0)
              {
                  await secondWorkStarted.Task;
                  throw new InvalidOperationException("Synthetic work-item failure.");
              }

              if (source == 1)
              {
                  secondWorkStarted.TrySetResult();
                  await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
              }

              return source;
          });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runTask);
        Assert.DoesNotContain(started, source => source >= 2);
    }

    [Fact]
    public void CommitOrdered_WhenWorkersFinishOutOfOrder_CommitsInputOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var committed = new List<int>();

        pool.CommitOrdered(
          new[] { 0, 1, 2 },
          new ConcurrencyWindowOptions(3),
          (source, _) => source,
          (result, _) => committed.Add(result));

        Assert.Equal(new[] { 0, 1, 2 }, committed);
    }

    [Fact]
    public async Task CommitOrdered_WhenHeadIsBlocked_DoesNotStartWorkBeyondTheReorderWindow()
    {
        var pool = new BoundedConcurrencyPool();
        var headWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHeadWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLookAheadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beyondWindowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = Task.Run(() => pool.CommitOrdered(
          new[] { 0, 1, 2 },
          new ConcurrencyWindowOptions(MaxDegreeOfParallelism: 3, ReorderAllowance: 1),
          (source, _) =>
          {
              if (source == 0)
              {
                  headWorkStarted.TrySetResult();
                  releaseHeadWork.Task.GetAwaiter().GetResult();
              }
              else if (source == 1)
              {
                  firstLookAheadStarted.TrySetResult();
              }
              else
              {
                  beyondWindowStarted.TrySetResult();
              }

              return source;
          },
          (_, _) => { }));

        await headWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await firstLookAheadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotSame(
          beyondWindowStarted.Task,
          await Task.WhenAny(beyondWindowStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(150))));

        releaseHeadWork.TrySetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(beyondWindowStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task ConcurrencyWindowOptions_WhenCreatingAnAdmissionRequest_ReservesItsWholeCompletionWindow()
    {
        var admissionController = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 2,
          MaxReservedByteCount: 16));
        var options = new ConcurrencyWindowOptions(
          MaxDegreeOfParallelism: 2,
          ReorderAllowance: 2,
          WorkClass: ConcurrencyWorkClass.Throughput,
          EstimatedRetainedBytesPerItem: 8);

        using var lease = await admissionController.AcquireAsync(options.CreateAdmissionRequest(sourceCount: 2));

        Assert.Equal(2, lease.Request.ReservedItemCount);
        Assert.Equal(16, lease.Request.ReservedByteCount);
        Assert.Equal(ConcurrencyWorkClass.Throughput, lease.Request.WorkClass);
    }

    [Fact]
    public async Task ConcurrencyWindowOptions_WhenItsByteWindowExceedsTheBudget_FailsBeforeAdmission()
    {
        var admissionController = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 2,
          MaxReservedByteCount: 15));
        var options = new ConcurrencyWindowOptions(
          MaxDegreeOfParallelism: 2,
          ReorderAllowance: 2,
          EstimatedRetainedBytesPerItem: 8);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
          admissionController.AcquireAsync(options.CreateAdmissionRequest(sourceCount: 2)));
    }

    [Fact]
    public void CommitTwoStageOrdered_WhenCollectAndSolveRunInWorkers_PreparesAndCommitsInputOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var prepared = new List<int>();
        var committed = new List<int>();

        pool.CommitTwoStageOrdered(
          new[] { 0, 1, 2 },
          new ConcurrencyWindowOptions(3),
          (source, _) => source,
          (collected, index) =>
          {
              prepared.Add(index);
              return collected * 2;
          },
          (value, _) => value + 1,
          (result, _) => committed.Add(result));

        Assert.Equal(new[] { 0, 1, 2 }, prepared);
        Assert.Equal(new[] { 1, 3, 5 }, committed);
    }

    [Fact]
    public async Task CommitOrdered_WhenReorderAllowanceIsZero_CommitsEverySourceInOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var committed = new List<int>();

        var operation = Task.Run(() => pool.CommitOrdered(
          new[] { 0, 1 },
          new ConcurrencyWindowOptions(MaxDegreeOfParallelism: 2, ReorderAllowance: 0),
          (source, _) => source,
          (result, _) => committed.Add(result)));

        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 0, 1 }, committed);
    }

    [Fact]
    public void CommitOrdered_WhenReorderAllowanceIsNegative_RejectsTheInvalidBoundary()
    {
        var pool = new BoundedConcurrencyPool();

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.CommitOrdered(
          new[] { 0 },
          new ConcurrencyWindowOptions(MaxDegreeOfParallelism: 1, ReorderAllowance: -1),
          (source, _) => source,
          (_, _) => { }));
    }

    [Fact]
    public void CommitOrdered_WhenWorkClassIsSpecified_ReportsTheDeclaredClassWithoutAdmission()
    {
        var telemetrySink = new RecordingTelemetrySink();
        var pool = new BoundedConcurrencyPool(telemetrySink);

        pool.CommitOrdered(
          new[] { 0 },
          new ConcurrencyWindowOptions(
            MaxDegreeOfParallelism: 1,
            WorkClass: ConcurrencyWorkClass.LatencySensitive),
          (source, _) => source,
          (_, _) => { });

        Assert.Equal(ConcurrencyWorkClass.LatencySensitive, Assert.Single(telemetrySink.Operations).WorkClass);
    }

    [Fact]
    public async Task CommitTwoStageOrdered_WhenReorderAllowanceIsZero_PreparesAndCommitsEverySourceInOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var prepared = new List<int>();
        var committed = new List<int>();

        var operation = Task.Run(() => pool.CommitTwoStageOrdered(
          new[] { 0, 1 },
          new ConcurrencyWindowOptions(MaxDegreeOfParallelism: 2, ReorderAllowance: 0),
          (source, _) => source,
          (collected, index) =>
          {
              prepared.Add(index);
              return collected;
          },
          (preparedValue, _) => preparedValue,
          (result, _) => committed.Add(result)));

        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 0, 1 }, prepared);
        Assert.Equal(new[] { 0, 1 }, committed);
    }

    [Fact]
    public async Task CommitTwoStageOrdered_WhenHeadIsBlocked_BoundsCompletedWorkWithoutSuppressingLookAhead()
    {
        var telemetrySink = new RecordingTelemetrySink();
        var pool = new BoundedConcurrencyPool(telemetrySink);
        var firstCollectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCollection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterCollectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new List<int>();

        var operation = Task.Run(() => pool.CommitTwoStageOrdered(
          new[] { 0, 1, 2 },
          new ConcurrencyWindowOptions(MaxDegreeOfParallelism: 3, ReorderAllowance: 1),
          (source, _) =>
          {
              if (source == 0)
              {
                  firstCollectionStarted.TrySetResult();
                  releaseFirstCollection.Task.GetAwaiter().GetResult();
              }
              else
              {
                  laterCollectionStarted.TrySetResult();
              }

              return source;
          },
          (collected, _) => collected,
          (prepared, _) => prepared,
          (result, _) => committed.Add(result)));

        try
        {
            await firstCollectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await laterCollectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseFirstCollection.TrySetResult();
        }

        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 0, 1, 2 }, committed);
        var telemetry = Assert.Single(telemetrySink.Operations);
        Assert.Equal(1, telemetry.PeakCompletedBufferItemCount);
    }

    [Fact]
    public async Task CommitTwoStageOrdered_WhenPrepareAndCommitHeadsOverlap_ReservesTheSharedBufferForCommit()
    {
        var telemetrySink = new RecordingTelemetrySink();
        var pool = new BoundedConcurrencyPool(telemetrySink);
        var secondCollectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSolveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSolve = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = Task.Run(() => pool.CommitTwoStageOrdered(
          new[] { 0, 1 },
          new ConcurrencyWindowOptions(MaxDegreeOfParallelism: 2, ReorderAllowance: 1),
          (source, _) =>
          {
              if (source == 1)
              {
                  secondCollectionStarted.TrySetResult();
              }

              return source;
          },
          (collected, _) => collected,
          (prepared, _) =>
          {
              if (prepared == 0)
              {
                  firstSolveStarted.TrySetResult();
                  releaseFirstSolve.Task.GetAwaiter().GetResult();
              }

              return prepared;
          },
          (_, _) => { }));

        await firstSolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await secondCollectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFirstSolve.TrySetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        var telemetry = Assert.Single(telemetrySink.Operations);
        Assert.Equal(1, telemetry.PeakCompletedBufferItemCount);
    }

    [Fact]
    public async Task RunDependencyGraphAsync_WhenDependenciesComplete_ExposesTheirResultsToDependentWork()
    {
        var pool = new BoundedConcurrencyPool();
        var workItems = new[]
        {
            new DependencyWorkItem<string, int>("A", Array.Empty<string>(), (_, _) => Task.FromResult(2)),
            new DependencyWorkItem<string, int>("B", Array.Empty<string>(), (_, _) => Task.FromResult(3)),
            new DependencyWorkItem<string, int>(
              "C",
              new[] { "A", "B" },
              (dependencies, _) => Task.FromResult(dependencies["A"] + dependencies["B"]))
        };

        var result = await pool.RunDependencyGraphAsync(
          workItems,
          maxDegreeOfParallelism: 2,
          StringComparer.Ordinal);

        Assert.Equal(5, result.Results["C"]);
        Assert.Equal(2, result.PeakConcurrentWorkItemCount);
    }

    [Fact]
    public async Task RunDependencyGraphAsync_WhenGraphContainsACycle_ThrowsAnInvalidOperationException()
    {
        var pool = new BoundedConcurrencyPool();
        var workItems = new[]
        {
            new DependencyWorkItem<string, int>("A", new[] { "B" }, (_, _) => Task.FromResult(1)),
            new DependencyWorkItem<string, int>("B", new[] { "A" }, (_, _) => Task.FromResult(2)),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          pool.RunDependencyGraphAsync(workItems, maxDegreeOfParallelism: 2, StringComparer.Ordinal));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunDependencyGraphAsync_WhenNestedGraphUsesTheSamePool_ReusesTheOuterAdmissionLease()
    {
        var admissionController = new ConcurrencyAdmissionController(new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: 1,
          MaxReservedItemCount: 1,
          MaxReservedByteCount: 0));
        var pool = new BoundedConcurrencyPool(telemetrySink: null, admissionController: admissionController);
        var innerWorkItems = new[]
        {
            new DependencyWorkItem<string, int>("inner", Array.Empty<string>(), (_, _) => Task.FromResult(7)),
        };
        var outerWorkItems = new[]
        {
            new DependencyWorkItem<string, int>(
              "outer",
              Array.Empty<string>(),
              async (_, _) =>
              {
                  var inner = await pool.RunDependencyGraphAsync(innerWorkItems, 1, StringComparer.Ordinal);
                  return inner.Results["inner"];
              }),
        };

        var result = await pool.RunDependencyGraphAsync(outerWorkItems, 1, StringComparer.Ordinal)
          .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(7, result.Results["outer"]);
    }

    [Fact]
    public async Task RunDependencyGraphAsync_WhenReadyNodeFails_CancelsRunningSiblingBeforeRethrowing()
    {
        var pool = new BoundedConcurrencyPool();
        var siblingCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workItems = new[]
        {
            new DependencyWorkItem<string, int>("failure", Array.Empty<string>(), (_, _) => throw new InvalidOperationException("failure")),
            new DependencyWorkItem<string, int>("sibling", Array.Empty<string>(), async (_, token) =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { siblingCanceled.TrySetResult(); throw; }
                return 0;
            })
        };

        var operation = pool.RunDependencyGraphAsync(workItems, 2, StringComparer.Ordinal);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
        await siblingCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunDependencyGraphAsync_WhenWorkFails_ReportsCancellationDrainTelemetry()
    {
        var telemetrySink = new RecordingTelemetrySink();
        var pool = new BoundedConcurrencyPool(telemetrySink);
        var siblingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workItems = new[]
        {
            new DependencyWorkItem<string, int>("failure", Array.Empty<string>(), (_, _) => throw new InvalidOperationException("failure")),
            new DependencyWorkItem<string, int>("sibling", Array.Empty<string>(), async (_, token) =>
            {
                siblingStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            })
        };

        var operation = pool.RunDependencyGraphAsync(workItems, 2, StringComparer.Ordinal);
        await siblingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await operation);

        var telemetry = Assert.Single(telemetrySink.Operations);
        Assert.Equal(ConcurrencyOperationKind.DependencyGraph, telemetry.OperationKind);
        Assert.Equal(ConcurrencyWorkClass.LatencySensitive, telemetry.WorkClass);
        Assert.Equal(2, telemetry.PeakReadyWorkItemCount);
        Assert.True(telemetry.WasCanceled);
        Assert.True(telemetry.FailureDrainElapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RunDependencyGraphAsync_WhenReadyNodeFails_CancelsRunningSiblingAndSkipsDependent()
    {
        var pool = new BoundedConcurrencyPool();
        using var callerCancellation = new CancellationTokenSource();
        var siblingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var siblingCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dependentStarted = false;
        var failure = new InvalidOperationException("Synthetic dependency-graph failure.");
        var workItems = new[]
        {
            new DependencyWorkItem<string, int>(
              "failing",
              Array.Empty<string>(),
              async (_, _) =>
              {
                  await siblingStarted.Task;
                  throw failure;
              }),
            new DependencyWorkItem<string, int>(
              "sibling",
              Array.Empty<string>(),
              async (_, cancellationToken) =>
              {
                  siblingStarted.TrySetResult();
                  try
                  {
                      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                  }
                  catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                  {
                      siblingCanceled.TrySetResult();
                      throw;
                  }

                  return 0;
              }),
            new DependencyWorkItem<string, int>(
              "dependent",
              new[] { "failing" },
              (_, _) =>
              {
                  dependentStarted = true;
                  return Task.FromResult(0);
              })
        };
        var runTask = pool.RunDependencyGraphAsync(
          workItems,
          maxDegreeOfParallelism: 2,
          StringComparer.Ordinal,
          callerCancellation.Token);

        try
        {
            var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(runTask, completed);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await runTask);
            Assert.Same(failure, exception);
            await siblingCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(dependentStarted);
        }
        finally
        {
            callerCancellation.Cancel();
            try
            {
                await runTask;
            }
            catch
            {
            }
        }
    }

    private sealed class RecordingTelemetrySink : IConcurrencyPoolTelemetrySink
    {
        public List<ConcurrencyOperationTelemetry> Operations { get; } = new();

        public void Record(ConcurrencyOperationTelemetry operation)
        {
            Operations.Add(operation);
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (candidate <= current || Interlocked.CompareExchange(ref target, candidate, current) == current)
                {
                    return;
                }
            }
        }
    }
}
