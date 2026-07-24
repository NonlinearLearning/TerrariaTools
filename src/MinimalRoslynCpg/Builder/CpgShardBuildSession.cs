using System.Diagnostics;
using System.Threading.Channels;
using MinimalRoslynCpg.Persistence;
using MinimalRoslynCpg.Persistence.Sqlite;

namespace MinimalRoslynCpg.Builder;

internal sealed class CpgShardBuildSession : IAsyncDisposable
{
  private const string CompletionMarkerFileName = "completed.marker";
  private static readonly AsyncLocal<Action<object>?> CheckpointObserverSlot = new();
  private readonly SqliteCpgShardCatalog _catalog;
  private readonly CpgCatalogBatchWriter _catalogWriter;
  private readonly CancellationTokenSource _publicationCancellation = new();
  private readonly Channel<CpgShardPublication> _publications;
  private readonly Channel<CpgShardPublicationResult> _writtenPublications;
  private readonly SemaphoreSlim _reorderSlots;
  private readonly Task _workersCompletion;
  private readonly Task _catalogDispatcher;
  private readonly IDisposable _storeLock;
  private readonly string _stagingRoot;
  private readonly long _storeLockWaitMilliseconds;
  private readonly List<CpgShardLocation> _stagedLocations = new();
  private readonly List<CpgReusableCloneRequest> _reusableCloneRequests = new();
  private readonly List<CpgBuildRoutingShardEntry> _routingEntries = new();
  private readonly HashSet<CpgShardLookup> _publishedLookups = new();
  private Exception? _publicationFault;
  private long _nextPublicationSequence;
  private long _publicationCount;
  private long _nextCatalogSequence;
  private int _activeFileWrites;
  private int _peakConcurrentFileWrites;
  private int _peakConcurrentShardExports;
  private int _peakReorderBuffer;
  private int _primaryShardCount;
  private int _boundaryAdjacencyShardCount;
  private long _primaryShardBytes;
  private long _boundaryAdjacencyShardBytes;
  private long _boundaryEdgeCount;
  private long _fileWriteMilliseconds;
  private long _serializationMilliseconds;
  private long _validationMilliseconds;
  private long _flushMilliseconds;
  private long _readBackMilliseconds;
  private long _hashMilliseconds;
  private long _structuralValidationMilliseconds;
  private int _reusedShardCount;
  private int _reuseMissCount;
  private int _reuseRejectedCount;
  private long _reusedShardBytes;
  private bool _completed;
  private Task? _disposeTask;
  private readonly object _disposeGate = new();

  private CpgShardBuildSession(
    CpgPersistenceOptions options,
    IDisposable storeLock,
    SqliteCpgShardCatalog catalog,
    string buildId,
    string stagingRoot,
    long storeLockWaitMilliseconds)
  {
    _storeLock = storeLock;
    _catalog = catalog;
    _catalogWriter = new CpgCatalogBatchWriter(catalog, buildId, options);
    _stagingRoot = stagingRoot;
    _storeLockWaitMilliseconds = storeLockWaitMilliseconds;
    BuildId = buildId;
    Store = new CpgShardStore(stagingRoot, options.DurabilityMode);
    _publications = Channel.CreateBounded<CpgShardPublication>(new BoundedChannelOptions(
      options.MaxPendingShardPublications)
    {
      FullMode = BoundedChannelFullMode.Wait,
      SingleWriter = false,
    });
    _writtenPublications = Channel.CreateBounded<CpgShardPublicationResult>(
      new BoundedChannelOptions(options.MaxPendingShardPublications)
      {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
      });
    _reorderSlots = new SemaphoreSlim(
      options.MaxPendingShardPublications,
      options.MaxPendingShardPublications);
    _workersCompletion = CompleteWorkersAsync(options.MaxConcurrentShardFileWrites);
    _catalogDispatcher = DispatchCatalogAsync();
  }

  internal string BuildId { get; }

  internal static Action<object>? CheckpointObserver
  {
    get => CheckpointObserverSlot.Value;
    set => CheckpointObserverSlot.Value = value;
  }

  internal CpgShardStore Store { get; }

  internal CpgPersistenceTelemetry Telemetry => new(
    PrimaryShardCount,
    BoundaryAdjacencyShardCount,
    PrimaryShardBytes,
    BoundaryAdjacencyShardBytes,
    BoundaryEdgeCount,
    _catalogWriter.CatalogRowCount,
    _catalogWriter.BatchCount,
    _catalogWriter.QueueWaitMilliseconds,
    _catalogWriter.CommitMilliseconds,
    FileWriteMilliseconds,
    _catalogWriter.PeakQueueDepth,
    PeakBufferedBoundaryEdges,
    SessionInvalidationCount,
    PeakConcurrentFileWrites,
    PeakConcurrentShardExports,
    _storeLockWaitMilliseconds,
    Interlocked.Read(ref _serializationMilliseconds),
    Interlocked.Read(ref _validationMilliseconds),
    Interlocked.Read(ref _flushMilliseconds),
    _catalogWriter.BatchRowCounts,
    Volatile.Read(ref _reusedShardCount),
    Volatile.Read(ref _reuseMissCount),
    Volatile.Read(ref _reuseRejectedCount),
    Interlocked.Read(ref _reusedShardBytes),
    Interlocked.Read(ref _readBackMilliseconds),
    Interlocked.Read(ref _hashMilliseconds),
    Interlocked.Read(ref _structuralValidationMilliseconds),
    _catalogWriter.BatchPublicationCounts,
    _catalogWriter.BatchEstimatedMetadataBytes,
    PeakReorderBuffer,
    CatalogActualRowCount: _catalogWriter.ActualRowCount,
    CatalogStatementCount: _catalogWriter.StatementCount,
    CatalogTransactionBeginMilliseconds: _catalogWriter.TransactionBeginMilliseconds,
    CatalogEnsureBuildingMilliseconds: _catalogWriter.EnsureBuildingMilliseconds,
    CatalogFixedMetadataMilliseconds: _catalogWriter.FixedMetadataMilliseconds,
    CatalogNodeWriteMilliseconds: _catalogWriter.NodeWriteMilliseconds,
    CatalogSpanWriteMilliseconds: _catalogWriter.SpanWriteMilliseconds,
    CatalogSymbolWriteMilliseconds: _catalogWriter.SymbolWriteMilliseconds,
    CatalogBoundaryWriteMilliseconds: _catalogWriter.BoundaryWriteMilliseconds,
    CatalogCommitTransactionMilliseconds: _catalogWriter.TransactionCommitMilliseconds,
    CatalogQueueSaturationMilliseconds: _catalogWriter.QueueSaturationMilliseconds,
    CatalogAffectedRowCount: _catalogWriter.AffectedRowCount,
    CatalogUnclassifiedMilliseconds: _catalogWriter.UnclassifiedMilliseconds,
    CatalogRowMaterializationMilliseconds: _catalogWriter.RowMaterializationMilliseconds,
    CatalogRowMaterializationAllocatedBytes: _catalogWriter.RowMaterializationAllocatedBytes,
    CatalogSqlTextBuildMilliseconds: _catalogWriter.SqlTextBuildMilliseconds,
    CatalogSqlTextBuildAllocatedBytes: _catalogWriter.SqlTextBuildAllocatedBytes,
    CatalogCommandPrepareMilliseconds: _catalogWriter.CommandPrepareMilliseconds,
    CatalogCommandPrepareAllocatedBytes: _catalogWriter.CommandPrepareAllocatedBytes,
    CatalogExecuteNonQueryMilliseconds: _catalogWriter.ExecuteNonQueryMilliseconds,
    CatalogExecuteNonQueryAllocatedBytes: _catalogWriter.ExecuteNonQueryAllocatedBytes);

  internal int PrimaryShardCount => Volatile.Read(ref _primaryShardCount);
  internal int BoundaryAdjacencyShardCount => Volatile.Read(ref _boundaryAdjacencyShardCount);
  internal long PrimaryShardBytes => Interlocked.Read(ref _primaryShardBytes);
  internal long BoundaryAdjacencyShardBytes => Interlocked.Read(ref _boundaryAdjacencyShardBytes);
  internal long BoundaryEdgeCount => Interlocked.Read(ref _boundaryEdgeCount);
  internal long FileWriteMilliseconds => Interlocked.Read(ref _fileWriteMilliseconds);
  internal int PeakBufferedBoundaryEdges { get; private set; }
  internal int PeakConcurrentFileWrites => Volatile.Read(ref _peakConcurrentFileWrites);
  internal int PeakConcurrentShardExports => Volatile.Read(ref _peakConcurrentShardExports);
  internal int PeakReorderBuffer => Volatile.Read(ref _peakReorderBuffer);
  internal int SessionInvalidationCount { get; private set; }

  internal void ObserveRouterBuffer(int bufferedEdges)
  {
    PeakBufferedBoundaryEdges = Math.Max(PeakBufferedBoundaryEdges, bufferedEdges);
  }

  internal void ObserveShardExport(int activeExports)
  {
    while (true)
    {
      var observed = Volatile.Read(ref _peakConcurrentShardExports);
      if (activeExports <= observed || Interlocked.CompareExchange(
        ref _peakConcurrentShardExports, activeExports, observed) == observed)
      {
        return;
      }
    }
  }

  internal static async Task<CpgShardBuildSession> BeginAsync(
    CpgPersistenceOptions options,
    CancellationToken cancellationToken)
  {
    var lockStopwatch = Stopwatch.StartNew();
    var storeLock = await CpgShardStoreLock.AcquireAsync(
      options.StoreRoot,
      TimeSpan.FromMilliseconds(options.StoreLockWaitMilliseconds),
      cancellationToken);
    lockStopwatch.Stop();
    try
    {
      var catalog = new SqliteCpgShardCatalog(Path.Combine(options.StoreRoot, "catalog.db"));
      var buildId = await catalog.BeginBuildAsync(cancellationToken);
      var stagingRoot = Path.Combine(options.StoreRoot, "builds", buildId);
      return new CpgShardBuildSession(
        options,
        storeLock,
        catalog,
        buildId,
        stagingRoot,
        lockStopwatch.ElapsedMilliseconds);
    }
    catch
    {
      storeLock.Dispose();
      throw;
    }
  }

  internal async Task PublishFragmentAsync(CpgFrozenShard shard, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(shard);
    ThrowIfPublicationFaulted();
    lock (_publishedLookups)
    {
      if (!_publishedLookups.Add(shard.Lookup))
      {
        return;
      }
    }

    var sourceSequence = Interlocked.Increment(ref _nextPublicationSequence) - 1;
    await EnqueuePublicationAsync(shard, sourceSequence, null, cancellationToken);
  }

  internal async Task PublishReusableFragmentAsync(
    CpgFrozenShard shard,
    CpgReusableFragmentKey reusableKey,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(reusableKey);
    ArgumentNullException.ThrowIfNull(shard);
    ThrowIfPublicationFaulted();
    lock (_publishedLookups)
    {
      if (!_publishedLookups.Add(shard.Lookup))
      {
        return;
      }
    }

    var sourceSequence = Interlocked.Increment(ref _nextPublicationSequence) - 1;
    await EnqueuePublicationAsync(shard, sourceSequence, reusableKey, cancellationToken);
  }

  internal async Task<bool> TryReuseFragmentAsync(
    CpgFrozenShard shard,
    CpgReusableFragmentKey reusableKey,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(shard);
    ArgumentNullException.ThrowIfNull(reusableKey);
    var candidate = await _catalog.TryAcquireReusableAsync(reusableKey, cancellationToken);
    if (candidate is null)
    {
      Interlocked.Increment(ref _reuseMissCount);
      return false;
    }

    try
    {
      var validation = Store.Validate(candidate.Location, cancellationToken);
      Interlocked.Add(ref _readBackMilliseconds, validation.ReadBackMilliseconds);
      Interlocked.Add(ref _hashMilliseconds, validation.HashMilliseconds);
      Interlocked.Add(ref _structuralValidationMilliseconds, validation.StructuralValidationMilliseconds);

      lock (_publishedLookups)
      {
        if (!_publishedLookups.Add(shard.Lookup))
        {
          return true;
        }
      }

      lock (_reusableCloneRequests)
      {
        _reusableCloneRequests.Add(new CpgReusableCloneRequest(shard.Lookup, candidate));
      }
      lock (_routingEntries)
      {
        _routingEntries.Add(new CpgBuildRoutingShardEntry(shard, candidate.Location));
      }
      Interlocked.Increment(ref _reusedShardCount);
      Interlocked.Add(ref _reusedShardBytes, candidate.Location.ByteLength);
      Interlocked.Increment(ref _primaryShardCount);
      Interlocked.Add(ref _primaryShardBytes, candidate.Location.ByteLength);
      return true;
    }
    catch (IOException)
    {
      Interlocked.Increment(ref _reuseRejectedCount);
      return false;
    }
    catch (InvalidDataException)
    {
      Interlocked.Increment(ref _reuseRejectedCount);
      return false;
    }
  }

  internal async Task PublishFragmentAsync(
    CpgFrozenShard shard,
    long sourceSequence,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(shard);
    ArgumentOutOfRangeException.ThrowIfNegative(sourceSequence);
    ThrowIfPublicationFaulted();
    lock (_publishedLookups)
    {
      if (!_publishedLookups.Add(shard.Lookup))
      {
        return;
      }
    }

    await EnqueuePublicationAsync(shard, sourceSequence, null, cancellationToken);
  }

  private async Task EnqueuePublicationAsync(
    CpgFrozenShard shard,
    long sourceSequence,
    CpgReusableFragmentKey? reusableKey,
    CancellationToken cancellationToken)
  {
    Interlocked.Increment(ref _publicationCount);
    try
    {
      await _publications.Writer.WriteAsync(
        new CpgShardPublication(sourceSequence, shard, reusableKey), cancellationToken);
    }
    catch
    {
      ThrowIfPublicationFaulted();
      throw;
    }
  }

  internal async Task CompleteAsync(CancellationToken cancellationToken)
  {
    _publications.Writer.TryComplete();
    await _workersCompletion.WaitAsync(cancellationToken);
    await _catalogDispatcher.WaitAsync(cancellationToken);
    ThrowIfPublicationFaulted();
    await _catalogWriter.CompleteAsync(cancellationToken);
    CpgReusableCloneRequest[] reusableCloneRequests;
    lock (_reusableCloneRequests)
    {
      reusableCloneRequests = _reusableCloneRequests.ToArray();
    }

    CpgBuildRoutingShardEntry[] routingEntries;
    lock (_routingEntries)
    {
      routingEntries = _routingEntries.ToArray();
    }

    var routingIndexPath = Path.Combine(_stagingRoot, "routing.cpgidx");
    var routingIndex = await new CpgBuildRoutingIndexWriter().WriteAsync(
      routingIndexPath,
      BuildId,
      routingEntries,
      cancellationToken);
    await _catalog.FinalizeBuildAsync(
      BuildId,
      reusableCloneRequests,
      new CpgBuildRoutingIndexManifest(
        Path.Combine("builds", BuildId, "routing.cpgidx"),
        routingIndex.FormatVersion,
        routingIndex.ByteLength,
        routingIndex.PayloadHash),
      cancellationToken);
    foreach (var request in reusableCloneRequests)
    {
      _stagedLocations.Add(request.Source.Location);
    }
    File.WriteAllText(
      Path.Combine(_stagingRoot, CompletionMarkerFileName),
      BuildId,
      new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    _completed = true;
  }

  public ValueTask DisposeAsync()
  {
    lock (_disposeGate)
    {
      _disposeTask ??= DisposeCoreAsync();
      return new ValueTask(_disposeTask);
    }
  }

  private async Task DisposeCoreAsync()
  {
    try
    {
      if (!_completed)
      {
        _publicationCancellation.Cancel();
        _publications.Writer.TryComplete();
        await DrainPublicationTasksAsync();
        SessionInvalidationCount += 1;
        await _catalog.InvalidateBuildAsync(BuildId, CancellationToken.None);
        if (Directory.Exists(_stagingRoot))
        {
          Directory.Delete(_stagingRoot, recursive: true);
        }
      }
    }
    finally
    {
      await _catalogWriter.DisposeAsync();
      _publicationCancellation.Dispose();
      _reorderSlots.Dispose();
      _storeLock.Dispose();
    }
  }

  private async Task CompleteWorkersAsync(int workerCount)
  {
    var workers = Enumerable.Range(0, workerCount)
      .Select(_ => WriteWorkerAsync())
      .ToArray();
    try
    {
      await Task.WhenAll(workers);
      _writtenPublications.Writer.TryComplete();
    }
    catch (Exception exception)
    {
      RecordPublicationFault(exception);
      _writtenPublications.Writer.TryComplete(exception);
      throw;
    }
  }

  private async Task WriteWorkerAsync()
  {
    try
    {
      await foreach (var publication in _publications.Reader.ReadAllAsync(_publicationCancellation.Token))
      {
        var activeWrites = Interlocked.Increment(ref _activeFileWrites);
        UpdatePeakConcurrentFileWrites(activeWrites);
        try
        {
          var stopwatch = Stopwatch.StartNew();
          var writeResult = await Store.WriteAsync(publication.Shard, _publicationCancellation.Token);
          stopwatch.Stop();
          Interlocked.Add(ref _fileWriteMilliseconds, stopwatch.ElapsedMilliseconds);
          RecordShardWrite(publication.Shard, writeResult);
          var consumesReorderSlot = publication.Sequence != Volatile.Read(ref _nextCatalogSequence);
          if (consumesReorderSlot)
          {
            await _reorderSlots.WaitAsync(_publicationCancellation.Token);
          }

          await _writtenPublications.Writer.WriteAsync(
            new CpgShardPublicationResult(
              publication.Sequence,
              publication.Shard,
              writeResult.Location,
              publication.ReusableKey,
              consumesReorderSlot),
            _publicationCancellation.Token);
          CheckpointObserver?.Invoke(new CpgShardBuildCheckpoint(
            BuildId,
            _stagingRoot,
            publication.Shard.Lookup.Fragment.Kind,
            publication.Shard.Lookup.Fragment.SpanStart,
            activeWrites));
        }
        finally
        {
          Interlocked.Decrement(ref _activeFileWrites);
        }
      }
    }
    catch (Exception exception)
    {
      RecordPublicationFault(exception);
      _publicationCancellation.Cancel();
      _publications.Writer.TryComplete(exception);
      throw;
    }
  }

  private async Task DispatchCatalogAsync()
  {
    var seenSequences = new HashSet<long>();
    var bufferedPublications = new SortedDictionary<long, CpgShardPublicationResult>();
    var nextSequence = 0L;
    try
    {
      await foreach (var publication in _writtenPublications.Reader.ReadAllAsync())
      {
        if (!seenSequences.Add(publication.Sequence))
        {
          throw new InvalidOperationException("CPG shard publication sequence was duplicated.");
        }

        bufferedPublications.Add(publication.Sequence, publication);
        UpdatePeakReorderBuffer(bufferedPublications.Count);
        while (bufferedPublications.Remove(nextSequence, out var nextPublication))
        {
          await _catalogWriter.EnqueueAsync(
            new CpgShardLease(nextPublication.Shard.Lookup, nextPublication.Location),
            nextPublication.Shard,
            nextPublication.ReusableKey,
            CancellationToken.None);
          _stagedLocations.Add(nextPublication.Location);
          lock (_routingEntries)
          {
            _routingEntries.Add(new CpgBuildRoutingShardEntry(
              nextPublication.Shard,
              nextPublication.Location));
          }
          if (nextPublication.ConsumesReorderSlot)
          {
            _reorderSlots.Release();
          }
          nextSequence += 1;
          Volatile.Write(ref _nextCatalogSequence, nextSequence);
        }
      }

      var expectedPublicationCount = Volatile.Read(ref _publicationCount);
      if (bufferedPublications.Count != 0 || nextSequence != expectedPublicationCount)
      {
        throw new InvalidOperationException("CPG shard publication sequence was incomplete.");
      }

      if (seenSequences.Count != expectedPublicationCount ||
          seenSequences.Any(sequence => sequence >= expectedPublicationCount))
      {
        throw new InvalidOperationException("CPG shard publication sequence was incomplete.");
      }
    }
    catch (Exception exception)
    {
      RecordPublicationFault(exception);
      _publicationCancellation.Cancel();
      throw;
    }
  }

  private async Task DrainPublicationTasksAsync()
  {
    try
    {
      await _workersCompletion;
    }
    catch
    {
    }

    try
    {
      await _catalogDispatcher;
    }
    catch
    {
    }
  }

  private void RecordShardWrite(CpgFrozenShard shard, CpgShardWriteResult result)
  {
    Interlocked.Add(ref _serializationMilliseconds, result.Telemetry.SerializationMilliseconds);
    Interlocked.Add(ref _validationMilliseconds, result.Telemetry.ValidationMilliseconds);
    Interlocked.Add(ref _flushMilliseconds, result.Telemetry.FlushMilliseconds);
    Interlocked.Add(ref _readBackMilliseconds, result.Telemetry.ReadBackMilliseconds);
    Interlocked.Add(ref _hashMilliseconds, result.Telemetry.HashMilliseconds);
    Interlocked.Add(ref _structuralValidationMilliseconds, result.Telemetry.StructuralValidationMilliseconds);
    var byteLength = result.Location.ByteLength;
    if (shard.Role == CpgShardRole.BoundaryAdjacency)
    {
      Interlocked.Increment(ref _boundaryAdjacencyShardCount);
      Interlocked.Add(ref _boundaryAdjacencyShardBytes, byteLength);
      Interlocked.Add(ref _boundaryEdgeCount, shard.BoundaryEdges?.Count ?? 0);
      return;
    }

    Interlocked.Increment(ref _primaryShardCount);
    Interlocked.Add(ref _primaryShardBytes, byteLength);
  }

  private void UpdatePeakConcurrentFileWrites(int activeWrites)
  {
    while (true)
    {
      var observed = Volatile.Read(ref _peakConcurrentFileWrites);
      if (activeWrites <= observed || Interlocked.CompareExchange(
        ref _peakConcurrentFileWrites, activeWrites, observed) == observed)
      {
        return;
      }
    }
  }

  private void UpdatePeakReorderBuffer(int bufferedPublications)
  {
    while (true)
    {
      var observed = Volatile.Read(ref _peakReorderBuffer);
      if (bufferedPublications <= observed || Interlocked.CompareExchange(
        ref _peakReorderBuffer,
        bufferedPublications,
        observed) == observed)
      {
        return;
      }
    }
  }

  private void RecordPublicationFault(Exception exception)
  {
    Interlocked.CompareExchange(ref _publicationFault, exception, null);
  }

  private void ThrowIfPublicationFaulted()
  {
    if (Volatile.Read(ref _publicationFault) is { } exception)
    {
      System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
    }
  }
}

internal sealed record CpgShardPublication(
  long Sequence,
  CpgFrozenShard Shard,
  CpgReusableFragmentKey? ReusableKey);

internal sealed record CpgShardPublicationResult(
  long Sequence,
  CpgFrozenShard Shard,
  CpgShardLocation Location,
  CpgReusableFragmentKey? ReusableKey,
  bool ConsumesReorderSlot);

internal sealed record CpgReusableCloneRequest(
  CpgShardLookup TargetLookup,
  CpgReusableShardLease Source);

internal sealed record CpgShardBuildCheckpoint(
  string BuildId,
  string StagingRoot,
  string FragmentKind,
  int FragmentSpanStart,
  int ActiveFileWriteCount);
