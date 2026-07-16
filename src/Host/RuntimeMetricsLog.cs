using System.Diagnostics;
using System.Text.Json;
using Rules;
using RoslynPrototype.Rewrite;

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

    internal void Complete(PrototypeAnalysisResult? result = null)
    {
        WriteTerminalRecord("completed", result);
    }

    internal void Fail()
    {
        WriteTerminalRecord("failed", result: null);
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

    private void WriteTerminalRecord(string status, PrototypeAnalysisResult? result)
    {
        lock (_writeLock)
        {
            if (_disposed || _terminalRecordWritten)
            {
                return;
            }

            _terminalRecordWritten = true;
            WriteRecord(status, CreateAnalysisSummary(result));
        }
    }

    private void WriteRecord(string status)
    {
        WriteRecord(status, analysisSummary: null);
    }

    private void WriteRecord(string status, object? analysisSummary)
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
            analysisSummary,
            recordedAtUtc = DateTime.UtcNow
        };
        _writer.WriteLine(JsonSerializer.Serialize(entry));
        _writer.Flush();
    }

    private static object? CreateAnalysisSummary(PrototypeAnalysisResult? result)
    {
        if (result is null)
        {
            return null;
        }

        var cpg = result.CpgBuildTelemetry;
        var freeze = cpg?.FreezeTelemetry;
        var syntax = cpg?.SyntaxPassTelemetry;
        var dataFlow = cpg?.DataFlowPassTelemetry;
        var mark = result.MarkAnalysisTelemetry;
        var structureView = result.StructureViewCacheTelemetry;
        var slowestMarkRule = mark?.RuleTelemetry
          .OrderByDescending(rule => rule.ElapsedMilliseconds)
          .FirstOrDefault();

        return new
        {
            timings = result.Timings is null
              ? null
              : new
              {
                  preparationMs = result.Timings.PreparationMilliseconds,
                  cpgBuildMs = result.Timings.CpgBuildMilliseconds,
                  markMs = result.Timings.MarkMilliseconds,
                  propagateMs = result.Timings.PropagateMilliseconds,
                  liftMs = result.Timings.LiftMilliseconds,
                  decideMs = result.Timings.DecideMilliseconds,
                  rewriteMs = result.Timings.RewriteMilliseconds,
                  analysisTotalMs = result.Timings.TotalMilliseconds
              },
            stats = result.Stats is null
              ? null
              : new
              {
                  scannedFileCount = result.Stats.ScannedFileCount,
                  analyzedFileCount = result.Stats.AnalyzedFileCount,
                  candidateMethodCount = result.Stats.CandidateMethodCount,
                  deletedMethodCount = result.Stats.DeletedMethodCount,
                  elapsedMs = result.Stats.ElapsedMilliseconds
              },
            cpg = cpg is null
              ? null
              : new
              {
                  graphNodeCount = cpg.GraphNodeCount,
                  graphEdgeCount = cpg.GraphEdgeCount,
                  partitionCount = cpg.PartitionCount,
                  operationChildBufferRentCount = cpg.OperationChildBufferRentCount,
                  maxDegreeOfParallelism = cpg.MaxDegreeOfParallelism,
                  operationElapsedMs = cpg.OperationBuildElapsedMilliseconds,
                  syntaxNodeCount = syntax?.SyntaxNodeCount ?? 0,
                  syntaxTokenCount = syntax?.SyntaxTokenCount ?? 0,
                  syntaxElapsedMs = cpg.SyntaxBuildElapsedMilliseconds,
                  dataFlowElapsedMs = cpg.DataFlowBuildElapsedMilliseconds,
                  freezeElapsedMs = cpg.FreezeQueryIndexElapsedMilliseconds,
                  freeze = freeze is null
                    ? null
                    : new
                    {
                        assignNodeIdsElapsedMs = freeze.AssignDeterministicNodeIdsElapsedMilliseconds,
                        createAnchorsElapsedMs = freeze.CreateAnchorsElapsedMilliseconds,
                        createNodeIdTableElapsedMs = freeze.CreateNodeIdTableElapsedMilliseconds,
                        remapNodesElapsedMs = freeze.RemapNodesElapsedMilliseconds,
                        remapEdgesElapsedMs = freeze.RemapEdgesElapsedMilliseconds,
                        buildQueryIndexElapsedMs = freeze.BuildQueryIndexElapsedMilliseconds,
                        populateEdgeBucketsElapsedMs = freeze.PopulateEdgeIndexBucketsElapsedMilliseconds,
                        orderEdgesElapsedMs = freeze.OrderEdgesElapsedMilliseconds,
                        orderNodesElapsedMs = freeze.OrderNodesElapsedMilliseconds,
                        snapshotHashElapsedMs = freeze.SnapshotHashElapsedMilliseconds,
                        buildAdjacencyElapsedMs = freeze.BuildAdjacencyElapsedMilliseconds,
                        buildKindAdjacencyElapsedMs = freeze.BuildKindAdjacencyElapsedMilliseconds,
                        buildEdgeKindIndexElapsedMs = freeze.BuildEdgeKindIndexElapsedMilliseconds,
                        buildNodeKindIndexElapsedMs = freeze.BuildNodeKindIndexElapsedMilliseconds,
                        buildFilePathIndexElapsedMs = freeze.BuildFilePathIndexElapsedMilliseconds,
                        distinctAnchorCount = freeze.DistinctAnchorCount,
                        nodeCount = freeze.NodeCount,
                        edgeCount = freeze.EdgeCount
                    },
                  dataFlowFlowNodeCount = dataFlow?.FlowNodeCount ?? 0,
                  dataFlowDefinitionFactCount = dataFlow?.DefinitionFactCount ?? 0,
                  dataFlowUsedFactCount = dataFlow?.UsedFactCount ?? 0,
                  dataFlowCandidateEdgeCount = dataFlow?.CandidateEdgeCount ?? 0,
                  dataFlowPeakBufferedCandidateBatchCount = dataFlow?.PeakBufferedCandidateBatchCount ?? 0,
                  dataFlowSkippedMethodCount = dataFlow?.SkippedMethodCount ?? 0
              },
            mark = mark is null
              ? null
              : new
              {
                  atomicCandidateIndexHitCount = mark.AtomicCandidateIndexHitCount,
                  atomicCandidateIndexMissCount = mark.AtomicCandidateIndexMissCount,
                  operationLookupCacheHitCount = mark.OperationLookupCacheHitCount,
                  operationLookupCacheMissCount = mark.OperationLookupCacheMissCount,
                  graphBindingIndexHitCount = mark.GraphBindingIndexHitCount,
                  graphBindingIndexMissCount = mark.GraphBindingIndexMissCount,
                  regionCacheHitCount = mark.RegionCacheHitCount,
                  regionCacheMissCount = mark.RegionCacheMissCount,
                  targetMatchCacheHitCount = mark.TargetMatchCacheHitCount,
                  targetMatchCacheMissCount = mark.TargetMatchCacheMissCount,
                  sliceQueryCacheHitCount = mark.SliceQueryCacheHitCount,
                  sliceQueryCacheMissCount = mark.SliceQueryCacheMissCount,
                  ruleCount = mark.RuleTelemetry.Count
              },
            slowestMarkRule = slowestMarkRule is null
              ? null
              : new
              {
                  ruleId = slowestMarkRule.RuleId,
                  groupKey = slowestMarkRule.GroupKey,
                  elapsedMs = slowestMarkRule.ElapsedMilliseconds,
                  candidateMarkCount = slowestMarkRule.CandidateMarkCount,
                  acceptedMarkCount = slowestMarkRule.AcceptedMarkCount,
                  graphBindingFallbackCount = slowestMarkRule.GraphBindingFallbackCount
              },
            structureView = structureView is null
              ? null
              : new
              {
                  requestCount = structureView.RequestCount,
                  cacheHitCount = structureView.CacheHitCount,
                  cacheMissCount = structureView.CacheMissCount,
                  uniqueFragmentSetCount = structureView.UniqueFragmentSetCount,
                  maxCachedViewCount = structureView.MaxCachedViewCount,
                  cacheHitRate = structureView.CacheHitRate
              }
        };
    }
}
