using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using NLCPG.Persistence;

namespace NLCPG.Persistence.Sqlite;

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
    private long _writeMilliseconds;
    private int _batchCount;
    private long _rowCount;
    private Exception? _fault;

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

    internal long WriteMilliseconds => Interlocked.Read(ref _writeMilliseconds);

    internal int BatchCount => Volatile.Read(ref _batchCount);

    internal long RowCount => Interlocked.Read(ref _rowCount);

    internal async Task EnqueueAsync(CpgShardLease lease, CpgFrozenShard shard, CpgReusableFragmentKey? reusableKey, CancellationToken cancellationToken)
    {
        await _queue.Writer.WriteAsync(
          new CpgCatalogPublication(lease, shard, reusableKey, _writeLegacyRoutingRows),
          cancellationToken);
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

    // 完成批写入后台任务，并在有故障时重新抛出首个异常。
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

    private async Task FlushAsync(Microsoft.Data.Sqlite.SqliteConnection connection, SqliteCpgCatalogCommandCache commandCache, List<CpgCatalogPublication> batch, int estimatedRows, int estimatedMetadataBytes)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        await _catalog.StageBatchAsync(connection, _buildId, batch, commandCache, CancellationToken.None);
        stopwatch.Stop();
        Interlocked.Add(ref _writeMilliseconds, stopwatch.ElapsedMilliseconds);
        Interlocked.Increment(ref _batchCount);
        Interlocked.Add(ref _rowCount, estimatedRows);
        batch.Clear();
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
