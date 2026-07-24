using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using MinimalRoslynCpg.Persistence;

namespace MinimalRoslynCpg.Persistence.Sqlite;

internal sealed class CpgCatalogBatchWriter : IAsyncDisposable
{
  private readonly SqliteCpgShardCatalog _catalog;
  private readonly string _buildId;
  private readonly int _maxRows;
  private readonly int _maxBytes;
  private readonly int _maxQueueDepth;
  private readonly bool _writeLegacyRoutingRows;
  private readonly Channel<CpgCatalogPublication> _queue;
  private readonly Task<Microsoft.Data.Sqlite.SqliteConnection> _connection;
  private readonly Task _writer;
  private readonly Stopwatch _lifetime = Stopwatch.StartNew();
  private readonly List<int> _batchRowCounts = new();
  private readonly List<int> _batchPublicationCounts = new();
  private readonly List<int> _batchEstimatedMetadataBytes = new();
  private Exception? _fault;
  private int _pending;
  private long _saturationStartedTimestamp;
  private long _queueSaturationMilliseconds;

  internal CpgCatalogBatchWriter(SqliteCpgShardCatalog catalog, string buildId, Builder.CpgPersistenceOptions options)
  {
    _catalog = catalog;
    _buildId = buildId;
    _maxRows = options.MaxCatalogBatchRows;
    _maxBytes = options.MaxCatalogBatchBytes;
    _maxQueueDepth = options.MaxPendingShardPublications;
    _writeLegacyRoutingRows = !options.UseMinimalRoutingCatalog;
    _queue = Channel.CreateBounded<CpgCatalogPublication>(new BoundedChannelOptions(options.MaxPendingShardPublications)
    {
      FullMode = BoundedChannelFullMode.Wait,
      SingleReader = true,
      SingleWriter = false,
    });
    _connection = _catalog.OpenBatchConnectionAsync(CancellationToken.None);
    _writer = WriteAsync();
  }

  internal int BatchCount { get; private set; }
  internal long CatalogRowCount { get; private set; }
  internal long CommitMilliseconds { get; private set; }
  internal int PeakQueueDepth { get; private set; }
  internal long QueueWaitMilliseconds { get; private set; }
  internal long QueueSaturationMilliseconds => Interlocked.Read(ref _queueSaturationMilliseconds);
  internal long ActualRowCount { get; private set; }
  internal long StatementCount { get; private set; }
  internal long TransactionBeginMilliseconds { get; private set; }
  internal long EnsureBuildingMilliseconds { get; private set; }
  internal long FixedMetadataMilliseconds { get; private set; }
  internal long NodeWriteMilliseconds { get; private set; }
  internal long SpanWriteMilliseconds { get; private set; }
  internal long SymbolWriteMilliseconds { get; private set; }
  internal long BoundaryWriteMilliseconds { get; private set; }
  internal long TransactionCommitMilliseconds { get; private set; }
  internal long AffectedRowCount { get; private set; }
  internal long UnclassifiedMilliseconds { get; private set; }
  internal long RowMaterializationMilliseconds { get; private set; }
  internal long RowMaterializationAllocatedBytes { get; private set; }
  internal long SqlTextBuildMilliseconds { get; private set; }
  internal long SqlTextBuildAllocatedBytes { get; private set; }
  internal long CommandPrepareMilliseconds { get; private set; }
  internal long CommandPrepareAllocatedBytes { get; private set; }
  internal long ExecuteNonQueryMilliseconds { get; private set; }
  internal long ExecuteNonQueryAllocatedBytes { get; private set; }
  internal IReadOnlyList<int> BatchRowCounts => _batchRowCounts;
  internal IReadOnlyList<int> BatchPublicationCounts => _batchPublicationCounts;
  internal IReadOnlyList<int> BatchEstimatedMetadataBytes => _batchEstimatedMetadataBytes;

  internal async Task EnqueueAsync(
    CpgShardLease lease,
    CpgFrozenShard shard,
    CpgReusableFragmentKey? reusableKey,
    CancellationToken cancellationToken)
  {
    var before = _lifetime.ElapsedMilliseconds;
    var pending = Interlocked.Increment(ref _pending);
    if (pending >= _maxQueueDepth)
    {
      Interlocked.CompareExchange(
        ref _saturationStartedTimestamp,
        Stopwatch.GetTimestamp(),
        comparand: 0);
    }
    PeakQueueDepth = Math.Max(PeakQueueDepth, Math.Min(_maxQueueDepth, pending));
    try
    {
      await _queue.Writer.WriteAsync(
        new CpgCatalogPublication(lease, shard, reusableKey, _writeLegacyRoutingRows),
        cancellationToken);
      QueueWaitMilliseconds += Math.Max(0, _lifetime.ElapsedMilliseconds - before);
    }
    catch
    {
      RecordDequeuedPublication();
      throw;
    }
  }

  internal async Task CompleteAsync(CancellationToken cancellationToken)
  {
    _queue.Writer.TryComplete();
    await _writer.WaitAsync(cancellationToken);
    if (_fault is not null)
    {
      System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_fault).Throw();
    }
  }

  public async ValueTask DisposeAsync()
  {
    _queue.Writer.TryComplete();
    try
    {
      await _writer;
    }
    catch
    {
    }
  }

  private async Task WriteAsync()
  {
    await using var connection = await _connection;
    await using var commandCache = new SqliteCpgCatalogCommandCache(connection);
    try
    {
      var batch = new List<CpgCatalogPublication>();
      var estimatedRows = 0;
      var estimatedMetadataBytes = 0;
      await foreach (var publication in _queue.Reader.ReadAllAsync())
      {
        var cost = EstimateCost(publication);
        batch.Add(publication);
        estimatedRows += cost.Rows;
        estimatedMetadataBytes += cost.MetadataBytes;
        RecordDequeuedPublication();
        if (estimatedRows >= _maxRows || estimatedMetadataBytes >= _maxBytes)
        {
          await FlushAsync(
            connection,
            commandCache,
            batch,
            estimatedRows,
            estimatedMetadataBytes);
          estimatedRows = 0;
          estimatedMetadataBytes = 0;
        }
      }

      await FlushAsync(connection, commandCache, batch, estimatedRows, estimatedMetadataBytes);
    }
    catch (Exception exception)
    {
      _fault = exception;
      _queue.Writer.TryComplete(exception);
      throw;
    }
  }

  private async Task FlushAsync(
    Microsoft.Data.Sqlite.SqliteConnection connection,
    SqliteCpgCatalogCommandCache commandCache,
    List<CpgCatalogPublication> batch,
    int estimatedRows,
    int estimatedMetadataBytes)
  {
    if (batch.Count == 0)
    {
      return;
    }

    var stopwatch = Stopwatch.StartNew();
    var stage = await _catalog.StageBatchAsync(connection, _buildId, batch, commandCache, CancellationToken.None);
    stopwatch.Stop();
    CommitMilliseconds += stopwatch.ElapsedMilliseconds;
    BatchCount += 1;
    CatalogRowCount += estimatedRows;
    _batchRowCounts.Add(estimatedRows);
    _batchPublicationCounts.Add(batch.Count);
    _batchEstimatedMetadataBytes.Add(estimatedMetadataBytes);
    ActualRowCount += stage.SqlRowCount;
    StatementCount += stage.SqlStatementCount;
    TransactionBeginMilliseconds += stage.TransactionBeginMilliseconds;
    EnsureBuildingMilliseconds += stage.EnsureBuildingMilliseconds;
    FixedMetadataMilliseconds += stage.FixedMetadataMilliseconds;
    NodeWriteMilliseconds += stage.NodeWriteMilliseconds;
    SpanWriteMilliseconds += stage.SpanWriteMilliseconds;
    SymbolWriteMilliseconds += stage.SymbolWriteMilliseconds;
    BoundaryWriteMilliseconds += stage.BoundaryWriteMilliseconds;
    TransactionCommitMilliseconds += stage.TransactionCommitMilliseconds;
    AffectedRowCount += stage.SqlAffectedRowCount;
    RowMaterializationMilliseconds += stage.RowMaterializationMilliseconds;
    RowMaterializationAllocatedBytes += stage.RowMaterializationAllocatedBytes;
    SqlTextBuildMilliseconds += stage.SqlTextBuildMilliseconds;
    SqlTextBuildAllocatedBytes += stage.SqlTextBuildAllocatedBytes;
    CommandPrepareMilliseconds += stage.CommandPrepareMilliseconds;
    CommandPrepareAllocatedBytes += stage.CommandPrepareAllocatedBytes;
    ExecuteNonQueryMilliseconds += stage.ExecuteNonQueryMilliseconds;
    ExecuteNonQueryAllocatedBytes += stage.ExecuteNonQueryAllocatedBytes;
    UnclassifiedMilliseconds += Math.Max(
      0,
      stopwatch.ElapsedMilliseconds - stage.ClassifiedMilliseconds);
    batch.Clear();
  }

  private void RecordDequeuedPublication()
  {
    var pending = Interlocked.Decrement(ref _pending);
    if (pending >= _maxQueueDepth)
    {
      return;
    }

    var started = Interlocked.Exchange(ref _saturationStartedTimestamp, 0);
    if (started != 0)
    {
      Interlocked.Add(ref _queueSaturationMilliseconds, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
  }

  private static CpgCatalogPublicationCost EstimateCost(CpgCatalogPublication publication)
  {
    var rows = 3;
    var metadataBytes = EstimateValueBytes(
      publication.Lease.Lookup.File.ProjectId,
      publication.Lease.Lookup.File.RelativePath,
      publication.Lease.Lookup.File.SourceHash,
      publication.Lease.Lookup.ProfileHash,
      publication.Lease.Location.ShardId,
      publication.Lease.Location.ShardPath,
      publication.Lease.Location.ShardHash,
      publication.Lease.Lookup.Fragment.Kind,
      publication.Lease.Lookup.Fragment.FragmentHash);
    if (publication.ReusableKey is { } reusableKey)
    {
      rows += 1;
      metadataBytes += EstimateValueBytes(reusableKey.NodeIdFingerprint);
    }

    if (publication.Shard.Role == CpgShardRole.Primary)
    {
      rows += 1;
    }
    else
    {
      var boundaryEdges = publication.Shard.BoundaryEdges
        ?? throw new InvalidOperationException("A boundary-adjacency shard must contain boundary edges.");
      rows += 1 + boundaryEdges
        .SelectMany(edge => new[] { edge.SourceNodeId, edge.TargetNodeId })
        .Distinct()
        .Count();
      metadataBytes += EstimateValueBytes(publication.Shard.BoundaryAdjacency!.OwnerFragmentId);
    }

    foreach (var node in publication.Shard.Nodes)
    {
      rows += 1;
      metadataBytes += EstimateValueBytes(node.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
      if (node.SpanStart.HasValue && node.SpanEnd.HasValue)
      {
        rows += 1;
      }
    }

    foreach (var symbol in publication.Shard.SymbolLocations)
    {
      rows += 1;
      metadataBytes += EstimateValueBytes(symbol.SymbolKey);
    }

    return new CpgCatalogPublicationCost(rows, metadataBytes + (rows * 16));
  }

  private static int EstimateValueBytes(params string?[] values)
  {
    return values.Sum(value => string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value));
  }
}

internal readonly record struct CpgCatalogPublicationCost(int Rows, int MetadataBytes);

internal sealed record CpgCatalogPublication(
  CpgShardLease Lease,
  CpgFrozenShard Shard,
  CpgReusableFragmentKey? ReusableKey = null,
  bool WriteLegacyRoutingRows = true);
