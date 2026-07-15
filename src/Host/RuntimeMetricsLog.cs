using System.Diagnostics;
using System.Text.Json;
using Rules;

namespace RoslynPrototype.Application;

internal sealed class RuntimeMetricsLog : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    private readonly object _writeLock = new();
    private readonly StreamWriter _writer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Timer _timer;
    private readonly int _maxDegreeOfParallelism;
    private readonly long _initialAllocatedBytes;
    private bool _disposed;
    private bool _terminalRecordWritten;

    private RuntimeMetricsLog(string filePath, int maxDegreeOfParallelism)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        _initialAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read));
        _timer = new Timer(
          static state => ((RuntimeMetricsLog)state!).WriteRunningRecord(),
          this,
          SampleInterval,
          SampleInterval);
    }

    internal static RuntimeMetricsLog? Create(
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime)
    {
        var filePath = DeletionApplicationOptions.ResolveRuntimeMetricsLogPath(options);
        return filePath is null
          ? null
          : new RuntimeMetricsLog(filePath, runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
    }

    internal void Complete()
    {
        WriteTerminalRecord("completed");
    }

    internal void Fail()
    {
        WriteTerminalRecord("failed");
    }

    public void Dispose()
    {
        _timer.Dispose();
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    private void WriteRunningRecord()
    {
        lock (_writeLock)
        {
            if (_disposed || _terminalRecordWritten)
            {
                return;
            }

            WriteRecord("running");
        }
    }

    private void WriteTerminalRecord(string status)
    {
        lock (_writeLock)
        {
            if (_disposed || _terminalRecordWritten)
            {
                return;
            }

            _terminalRecordWritten = true;
            WriteRecord(status);
        }
    }

    private void WriteRecord(string status)
    {
        ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);
        ThreadPool.GetMaxThreads(out var maximumWorkerThreads, out var maximumCompletionPortThreads);
        using var process = Process.GetCurrentProcess();
        var entry = new
        {
            status,
            maxDegreeOfParallelism = _maxDegreeOfParallelism,
            elapsedMs = _stopwatch.ElapsedMilliseconds,
            allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - _initialAllocatedBytes,
            gen0CollectionCount = GC.CollectionCount(0),
            gen1CollectionCount = GC.CollectionCount(1),
            gen2CollectionCount = GC.CollectionCount(2),
            managedHeapBytes = GC.GetGCMemoryInfo().HeapSizeBytes,
            workingSetBytes = process.WorkingSet64,
            threadPoolThreadCount = ThreadPool.ThreadCount,
            threadPoolPendingWorkItemCount = ThreadPool.PendingWorkItemCount,
            threadPoolCompletedWorkItemCount = ThreadPool.CompletedWorkItemCount,
            threadPoolAvailableWorkerThreads = availableWorkerThreads,
            threadPoolMaximumWorkerThreads = maximumWorkerThreads,
            threadPoolAvailableCompletionPortThreads = availableCompletionPortThreads,
            threadPoolMaximumCompletionPortThreads = maximumCompletionPortThreads,
            recordedAtUtc = DateTime.UtcNow
        };
        _writer.WriteLine(JsonSerializer.Serialize(entry));
        _writer.Flush();
    }
}
