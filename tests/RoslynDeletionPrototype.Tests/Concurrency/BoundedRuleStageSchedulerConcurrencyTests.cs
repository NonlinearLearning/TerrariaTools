using Rules;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests.Concurrency;

public sealed class BoundedRuleStageSchedulerConcurrencyTests
{
    private const int ConcurrentTrafficTestIterations = 10_000;
    private const string TerrariaExternalCodeSetPath =
      @"D:\lodes\TR\Backup\New1.27\1.45 2\TR";
    private readonly ITestOutputHelper _output;

    public BoundedRuleStageSchedulerConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RunOrderedAsync_OnlyStartsTheFirstWorkerWindowBeforeAnyWorkCompletes()
    {
        const int itemCount = 100;
        const int maxDegreeOfParallelism = 4;
        var scheduler = new BoundedRuleStageScheduler();
        var firstWindowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;

        var runTask = scheduler.RunOrderedAsync(
          itemCount,
          maxDegreeOfParallelism,
          async (index, cancellationToken) =>
          {
              if (Interlocked.Increment(ref startedCount) == maxDegreeOfParallelism)
              {
                  firstWindowStarted.TrySetResult();
              }

              await release.Task.WaitAsync(cancellationToken);
              return index;
          },
          CancellationToken.None);

        await firstWindowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(maxDegreeOfParallelism, Volatile.Read(ref startedCount));

        release.TrySetResult();
        var results = await runTask;

        Assert.Equal(Enumerable.Range(0, itemCount), results);
    }

    [Fact]
    public async Task RunOrderedAsync_ComputesPeakConcurrentTraffic()
    {
        var cases = new ConcurrentTrafficCase[]
        {
            new("empty", 0, 4, 0),
            new("single-unit", 1, 4, 1),
            new("below-capacity", 2, 4, 2),
            new("above-capacity", 5, 2, 2),
            new("multi-wave", 7, 3, 3),
            new("invalid-capacity-normalized", 3, 0, 1)
        };
        var scheduler = new BoundedRuleStageScheduler();
        WriteConcurrentTrafficLine(
          $"start iterations={ConcurrentTrafficTestIterations};cases={cases.Length}");

        for (var iteration = 1; iteration <= ConcurrentTrafficTestIterations; iteration++)
        {
            foreach (var currentCase in cases)
            {
                var probe = new ConcurrentTrafficProbe(
                  currentCase.ItemCount,
                  currentCase.MaxDegreeOfParallelism);

                var results = await scheduler.RunOrderedAsync(
                  currentCase.ItemCount,
                  currentCase.MaxDegreeOfParallelism,
                  probe.RunWorkItemAsync,
                  CancellationToken.None);

                Assert.Equal(currentCase.ExpectedPeakConcurrentTraffic, probe.PeakActiveCount);
                Assert.Equal(Enumerable.Range(0, currentCase.ItemCount).ToArray(), results.ToArray());
                Assert.Equal(currentCase.ItemCount, probe.CompletedCount);
            }

            if (iteration == 1 || iteration % 1_000 == 0)
            {
                WriteConcurrentTrafficLine(
                  $"progress iteration={iteration};total={ConcurrentTrafficTestIterations}");
            }
        }

        WriteConcurrentTrafficLine(
          $"done iterations={ConcurrentTrafficTestIterations};scheduledWorkItems={cases.Sum(currentCase => currentCase.ItemCount) * ConcurrentTrafficTestIterations}");
    }

    [Fact]
    public async Task RunOrderedAsync_PreservesOrderWhenAsyncWorkCompletesOutOfOrder()
    {
        const int itemCount = 5;
        var scheduler = new BoundedRuleStageScheduler();
        var releases = Enumerable.Range(0, itemCount)
          .Select(_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously))
          .ToArray();
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;

        var runTask = scheduler.RunOrderedAsync(
          itemCount,
          itemCount,
          async (index, cancellationToken) =>
          {
              if (Interlocked.Increment(ref startedCount) == itemCount)
              {
                  allStarted.TrySetResult();
              }

              return await releases[index].Task.WaitAsync(cancellationToken);
          },
          CancellationToken.None);

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var index = itemCount - 1; index >= 0; index--)
        {
            releases[index].SetResult(index * 10);
        }

        var results = await runTask;

        Assert.Equal(new[] { 0, 10, 20, 30, 40 }, results.ToArray());
    }

    [Fact]
    public async Task RunOrderedAsync_CancelsAwaitingAsyncWorkItems()
    {
        var scheduler = new BoundedRuleStageScheduler();
        using var cancellation = new CancellationTokenSource();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;

        var runTask = scheduler.RunOrderedAsync<int>(
          4,
          2,
          async (_, cancellationToken) =>
          {
              Interlocked.Increment(ref startedCount);
              firstStarted.TrySetResult();
              await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
              return 0;
          },
          cancellation.Token);

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runTask);
        Assert.InRange(startedCount, 1, 2);
    }

    [Fact]
    public async Task RunOrderedAsync_TerrariaCodeSet_StressesAsyncAndConcurrentScheduling()
    {
        const int maxDegreeOfParallelism = 7;
        var files = LoadTerrariaCodeSetFiles();
        Assert.True(
          files.Count > maxDegreeOfParallelism * 100,
          $"Expected a large external code set at {TerrariaExternalCodeSetPath}.");
        var scheduler = new BoundedRuleStageScheduler();
        var releases = files
          .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
          .ToArray();
        var probe = new CodeSetAsyncConcurrencyProbe(files.Count, maxDegreeOfParallelism);
        WriteConcurrentTrafficLine(
          $"terraria-start files={files.Count};mdop={maxDegreeOfParallelism};root={TerrariaExternalCodeSetPath}");

        var runTask = scheduler.RunOrderedAsync(
          files.Count,
          maxDegreeOfParallelism,
          async (index, cancellationToken) =>
          {
              await probe.EnterAsync(releases[index].Task, cancellationToken);
              try
              {
                  return await ReadCodeSetWorkItemAsync(files[index], index, cancellationToken);
              }
              finally
              {
                  probe.Leave();
              }
          },
          CancellationToken.None);

        await probe.FirstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        for (var index = releases.Length - 1; index >= 0; index--)
        {
            releases[index].TrySetResult();
        }

        var results = await runTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(files.Count, probe.StartedCount);
        Assert.Equal(files.Count, probe.CompletedCount);
        Assert.Equal(maxDegreeOfParallelism, probe.PeakActiveCount);
        AssertOrderedCodeSetResults(files, results);
        WriteConcurrentTrafficLine(
          $"terraria-done files={files.Count};peak={probe.PeakActiveCount};completed={probe.CompletedCount}");
    }

    [Fact]
    public async Task RunOrderedAsync_TerrariaCodeSet_MixesFastSlowAndAsyncIoWorkItems()
    {
        const int itemCount = 257;
        const int maxDegreeOfParallelism = 9;
        var files = LoadTerrariaCodeSetFiles().Take(itemCount).ToArray();
        Assert.Equal(itemCount, files.Length);
        var scheduler = new BoundedRuleStageScheduler();
        var releases = files
          .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
          .ToArray();
        var probe = new CodeSetAsyncConcurrencyProbe(files.Length, maxDegreeOfParallelism);

        var runTask = scheduler.RunOrderedAsync(
          files.Length,
          maxDegreeOfParallelism,
          async (index, cancellationToken) =>
          {
              await probe.EnterAsync(releases[index].Task, cancellationToken);
              try
              {
                  if (index % 3 == 0)
                  {
                      return new CodeSetWorkResult(index, files[index].RelativePath, files[index].Length, 0);
                  }

                  if (index % 3 == 1)
                  {
                      await Task.Yield();
                  }

                  return await ReadCodeSetWorkItemAsync(files[index], index, cancellationToken);
              }
              finally
              {
                  probe.Leave();
              }
          },
          CancellationToken.None);

        await probe.FirstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        ReleaseInInterleavedOrder(releases);

        var results = await runTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(maxDegreeOfParallelism, probe.PeakActiveCount);
        Assert.Equal(files.Length, probe.StartedCount);
        Assert.Equal(files.Length, probe.CompletedCount);
        AssertOrderedCodeSetResults(files, results, allowZeroBytesRead: true);
        WriteConcurrentTrafficLine(
          $"terraria-mixed-done files={files.Length};peak={probe.PeakActiveCount};completed={probe.CompletedCount}");
    }

    [Fact]
    public async Task RunOrderedAsync_TerrariaCodeSet_PropagatesAsyncWorkItemFailure()
    {
        const int itemCount = 64;
        const int maxDegreeOfParallelism = 8;
        const int failingIndex = 31;
        var files = LoadTerrariaCodeSetFiles().Take(itemCount).ToArray();
        var scheduler = new BoundedRuleStageScheduler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
          async () => await scheduler.RunOrderedAsync(
            files.Length,
            maxDegreeOfParallelism,
            async (index, cancellationToken) =>
            {
                await Task.Yield();
                if (index == failingIndex)
                {
                    throw new InvalidOperationException($"Synthetic async failure at {files[index].RelativePath}");
                }

                return await ReadCodeSetWorkItemAsync(files[index], index, cancellationToken);
            },
            CancellationToken.None));

        Assert.Contains(files[failingIndex].RelativePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunOrderedAsync_TerrariaCodeSet_CancelsQueuedAsyncWork()
    {
        const int maxDegreeOfParallelism = 7;
        var files = LoadTerrariaCodeSetFiles();
        Assert.True(
          files.Count > maxDegreeOfParallelism * 100,
          $"Expected a large external code set at {TerrariaExternalCodeSetPath}.");
        var scheduler = new BoundedRuleStageScheduler();
        using var cancellation = new CancellationTokenSource();
        var firstWaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;

        var runTask = scheduler.RunOrderedAsync<int>(
          files.Count,
          maxDegreeOfParallelism,
          async (_, cancellationToken) =>
          {
              if (Interlocked.Increment(ref startedCount) == maxDegreeOfParallelism)
              {
                  firstWaveStarted.TrySetResult();
              }

              await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
              return 0;
          },
          cancellation.Token);

        await firstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runTask);
        Assert.Equal(maxDegreeOfParallelism, startedCount);
        WriteConcurrentTrafficLine(
          $"terraria-cancelled files={files.Count};started={startedCount};mdop={maxDegreeOfParallelism}");
    }

    private sealed record ConcurrentTrafficCase(
      string Name,
      int ItemCount,
      int MaxDegreeOfParallelism,
      int ExpectedPeakConcurrentTraffic);

    private sealed record CodeSetWorkItem(
      int Index,
      string FullPath,
      string RelativePath,
      long Length);

    private sealed record CodeSetWorkResult(
      int Index,
      string RelativePath,
      long Length,
      int BytesRead);

    private void WriteConcurrentTrafficLine(string message)
    {
        var line = $"[concurrent-traffic] {message}";
        _output.WriteLine(line);
        Console.WriteLine(line);
    }

    private static IReadOnlyList<CodeSetWorkItem> LoadTerrariaCodeSetFiles()
    {
        Assert.True(
          Directory.Exists(TerrariaExternalCodeSetPath),
          $"Missing external code set: {TerrariaExternalCodeSetPath}");

        return Directory.EnumerateFiles(TerrariaExternalCodeSetPath, "*.cs", SearchOption.AllDirectories)
          .Where(path => !IsIgnoredCodeSetPath(path))
          .Select(path => new FileInfo(path))
          .OrderByDescending(file => file.Length)
          .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
          .Select((file, index) => new CodeSetWorkItem(
            index,
            file.FullName,
            Path.GetRelativePath(TerrariaExternalCodeSetPath, file.FullName),
            file.Length))
          .ToList();
    }

    private static bool IsIgnoredCodeSetPath(string path)
    {
        var relativePath = Path.GetRelativePath(TerrariaExternalCodeSetPath, path);
        return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
          .Any(part =>
            string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<CodeSetWorkResult> ReadCodeSetWorkItemAsync(
      CodeSetWorkItem item,
      int index,
      CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(512, Math.Max(1, item.Length))];
        await using var stream = new FileStream(
          item.FullPath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.ReadWrite | FileShare.Delete,
          bufferSize: buffer.Length,
          useAsync: true);
        var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
        return new CodeSetWorkResult(index, item.RelativePath, item.Length, bytesRead);
    }

    private static void ReleaseInInterleavedOrder(IReadOnlyList<TaskCompletionSource> releases)
    {
        for (var index = releases.Count - 1; index >= 0; index -= 2)
        {
            releases[index].TrySetResult();
        }

        for (var index = releases.Count - 2; index >= 0; index -= 2)
        {
            releases[index].TrySetResult();
        }
    }

    private static void AssertOrderedCodeSetResults(
      IReadOnlyList<CodeSetWorkItem> files,
      IReadOnlyList<CodeSetWorkResult> results,
      bool allowZeroBytesRead = false)
    {
        Assert.Equal(files.Count, results.Count);
        Assert.Equal(Enumerable.Range(0, files.Count).ToArray(), results.Select(result => result.Index).ToArray());
        Assert.All(
          results,
          result =>
          {
              var source = files[result.Index];
              Assert.Equal(source.RelativePath, result.RelativePath);
              Assert.Equal(source.Length, result.Length);
              if (allowZeroBytesRead && result.BytesRead == 0)
              {
                  return;
              }

              Assert.InRange(result.BytesRead, Math.Min(1, source.Length), Math.Min(512, source.Length));
          });
    }

    private sealed class ConcurrentTrafficProbe
    {
        private readonly TaskCompletionSource _releaseWave = new(
          TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _firstWaveSize;
        private int _activeCount;
        private int _completedCount;
        private int _peakActiveCount;
        private int _startedCount;

        public ConcurrentTrafficProbe(int itemCount, int maxDegreeOfParallelism)
        {
            _firstWaveSize = itemCount == 0
              ? 0
              : Math.Min(itemCount, Math.Max(1, maxDegreeOfParallelism));
        }

        public int CompletedCount => _completedCount;

        public int PeakActiveCount => _peakActiveCount;

        public async Task<int> RunWorkItemAsync(int index, CancellationToken cancellationToken)
        {
            var activeCount = Interlocked.Increment(ref _activeCount);
            RecordPeak(activeCount);

            if (Interlocked.Increment(ref _startedCount) == _firstWaveSize)
            {
                _releaseWave.TrySetResult();
            }

            try
            {
                if (_firstWaveSize > 1)
                {
                    await _releaseWave.Task.WaitAsync(cancellationToken);
                }

                return index;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
                Interlocked.Increment(ref _completedCount);
            }
        }

        private void RecordPeak(int activeCount)
        {
            while (true)
            {
                var observedPeak = _peakActiveCount;
                if (activeCount <= observedPeak)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _peakActiveCount, activeCount, observedPeak) ==
                    observedPeak)
                {
                    return;
                }
            }
        }
    }

    private sealed class CodeSetAsyncConcurrencyProbe
    {
        private readonly int _firstWaveSize;
        private int _activeCount;
        private int _completedCount;
        private int _peakActiveCount;
        private int _startedCount;

        public CodeSetAsyncConcurrencyProbe(int itemCount, int maxDegreeOfParallelism)
        {
            _firstWaveSize = Math.Min(itemCount, Math.Max(1, maxDegreeOfParallelism));
        }

        public TaskCompletionSource FirstWaveStarted { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CompletedCount => _completedCount;

        public int PeakActiveCount => _peakActiveCount;

        public int StartedCount => _startedCount;

        public async Task EnterAsync(Task release, CancellationToken cancellationToken)
        {
            var activeCount = Interlocked.Increment(ref _activeCount);
            RecordPeak(activeCount);
            if (Interlocked.Increment(ref _startedCount) == _firstWaveSize)
            {
                FirstWaveStarted.TrySetResult();
            }

            await release.WaitAsync(cancellationToken);
        }

        public void Leave()
        {
            Interlocked.Decrement(ref _activeCount);
            Interlocked.Increment(ref _completedCount);
        }

        private void RecordPeak(int activeCount)
        {
            while (true)
            {
                var observedPeak = _peakActiveCount;
                if (activeCount <= observedPeak)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _peakActiveCount, activeCount, observedPeak) ==
                    observedPeak)
                {
                    return;
                }
            }
        }
    }
}
