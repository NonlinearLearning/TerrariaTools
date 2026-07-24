using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace MinimalRoslynCpg.Persistence.Sqlite;

public sealed class SqliteCpgShardCatalog : ICpgShardCatalog
{
  private const string CompletionMarkerFileName = "completed.marker";
  private readonly string _connectionString;
  private readonly string _storeRoot;
  private readonly SemaphoreSlim _schemaInitializationGate = new(1, 1);
  private int _schemaInitialized;

  public SqliteCpgShardCatalog(string databasePath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
    _storeRoot = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
    _connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = databasePath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      // The catalog is frequently created for short-lived analysis stores. Retained pooled
      // handles prevent deterministic cleanup of those stores on Windows.
      Pooling = false,
    }.ToString();
  }

  public async Task<CpgShardLease?> TryAcquireAsync(CpgShardLookup lookup, CancellationToken cancellationToken)
  {
    var sessionLease = await TryAcquireSessionAsync(lookup, cancellationToken);
    if (sessionLease is not null)
    {
      return sessionLease;
    }

    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM files f
      JOIN fragments g ON g.file_id = f.file_id
      JOIN shards s ON s.fragment_id = g.fragment_id
      JOIN projects p ON p.project_id = f.project_id
      WHERE f.project_id = $projectId AND f.relative_path = $relativePath
        AND f.source_hash = $sourceHash AND g.fragment_kind = $fragmentKind
        AND g.span_start = $spanStart AND g.span_length = $spanLength
        AND g.fragment_hash = $fragmentHash AND p.schema_version = $schemaVersion
        AND p.builder_profile_hash = $profileHash AND f.status = 'Complete'
        AND g.status = 'Complete' AND s.status = 'Complete'
      ORDER BY s.shard_id LIMIT 1;
      """;
    BindLookup(command, lookup);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }

    var location = new CpgShardLocation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), CpgShardStatus.Complete);
    return new CpgShardLease(lookup, location);
  }

  public async Task<CpgReusableShardLease?> TryAcquireReusableAsync(
    CpgReusableFragmentKey key,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(key);
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT r.build_id, s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM session_reusable_fragments r
      JOIN build_sessions b ON b.build_id = r.build_id
      JOIN session_shards s ON s.build_id = r.build_id AND s.shard_id = r.shard_id
      WHERE b.status = 'Complete' AND r.project_id = $projectId
        AND r.relative_path = $relativePath AND r.schema_version = $schemaVersion
        AND r.builder_profile_hash = $profileHash AND r.fragment_kind = $fragmentKind
        AND r.span_start = $spanStart AND r.span_length = $spanLength
        AND r.fragment_hash = $fragmentHash AND r.node_id_fingerprint = $nodeIdFingerprint
      ORDER BY b.completed_at_utc DESC, b.build_id DESC, s.shard_id LIMIT 1;
      """;
    BindReusableKey(command, key);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }

    var location = new CpgShardLocation(
      reader.GetString(1),
      reader.GetString(2),
      reader.GetString(3),
      reader.GetInt64(4),
      CpgShardStatus.Complete);
    return new CpgReusableShardLease(key, location, reader.GetString(0));
  }

  public async Task<IReadOnlyList<CpgShardLocation>> FindBySymbolAsync(CpgSymbolLookup lookup, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(lookup);
    var routingLocations = await FindBySymbolRoutingIndexAsync(lookup, cancellationToken);
    if (routingLocations is not null)
    {
      return routingLocations;
    }

    var sessionLocations = await FindSessionBySymbolAsync(lookup, cancellationToken);
    if (sessionLocations.Count > 0)
    {
      return sessionLocations;
    }

    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT DISTINCT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM symbol_locations l JOIN shards s ON s.shard_id = l.shard_id
      WHERE l.symbol_key = $symbolKey AND s.status = 'Complete' ORDER BY s.shard_id;
      """;
    command.Parameters.AddWithValue("$symbolKey", lookup.SymbolKey);
    return await ReadLocationsAsync(command, cancellationToken);
  }

  public async Task<IReadOnlyList<CpgShardLocation>> FindByNodeAsync(
    uint nodeId,
    CancellationToken cancellationToken)
  {
    var routingLocations = await FindByNodeRoutingIndexAsync(nodeId, cancellationToken);
    if (routingLocations is not null)
    {
      return routingLocations;
    }

    var sessionLocations = await FindSessionByNodeAsync(nodeId, cancellationToken);
    if (sessionLocations.Count > 0)
    {
      return sessionLocations;
    }

    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      WITH primary_files AS (
        SELECT DISTINCT f.file_id
        FROM node_locations l
        JOIN shards s ON s.shard_id = l.shard_id
        JOIN fragments g ON g.fragment_id = s.fragment_id
        JOIN files f ON f.file_id = g.file_id
        WHERE l.node_id = $nodeId AND s.status = 'Complete')
      SELECT DISTINCT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM node_locations l JOIN shards s ON s.shard_id = l.shard_id
      WHERE l.node_id = $nodeId AND s.status = 'Complete'
      UNION
      SELECT DISTINCT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM primary_files f
      JOIN fragments g ON g.file_id = f.file_id
      JOIN shards s ON s.fragment_id = g.fragment_id
      WHERE g.fragment_kind = 'cross-shard-edges' AND s.status = 'Complete'
      ORDER BY 1;
      """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    return await ReadLocationsAsync(command, cancellationToken);
  }

  public async Task<IReadOnlyList<CpgShardLocation>> FindByFileAsync(
    CpgFileKey fileKey,
    int schemaVersion,
    string profileHash,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(fileKey);
    ArgumentException.ThrowIfNullOrWhiteSpace(profileHash);
    var sessionLocations = await FindSessionByFileAsync(
      fileKey,
      schemaVersion,
      profileHash,
      cancellationToken);
    if (sessionLocations.Count > 0)
    {
      return sessionLocations;
    }

    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT DISTINCT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM files f
      JOIN fragments g ON g.file_id = f.file_id
      JOIN shards s ON s.fragment_id = g.fragment_id
      JOIN projects p ON p.project_id = f.project_id
      WHERE f.project_id = $projectId AND f.relative_path = $relativePath
        AND f.source_hash = $sourceHash AND p.schema_version = $schemaVersion
        AND p.builder_profile_hash = $profileHash AND f.status = 'Complete'
        AND g.status = 'Complete' AND s.status = 'Complete'
      ORDER BY s.shard_id;
      """;
    command.Parameters.AddWithValue("$projectId", fileKey.ProjectId);
    command.Parameters.AddWithValue("$relativePath", fileKey.RelativePath);
    command.Parameters.AddWithValue("$sourceHash", fileKey.SourceHash);
    command.Parameters.AddWithValue("$schemaVersion", schemaVersion);
    command.Parameters.AddWithValue("$profileHash", profileHash);
    return await ReadLocationsAsync(command, cancellationToken);
  }

  public async Task<IReadOnlyList<CpgShardLocation>> FindBySpanAsync(CpgSpanLookup lookup, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(lookup);
    var routingLocations = await FindBySpanRoutingIndexAsync(lookup, cancellationToken);
    if (routingLocations is not null)
    {
      return routingLocations;
    }

    var sessionLocations = await FindSessionBySpanAsync(lookup, cancellationToken);
    if (sessionLocations.Count > 0)
    {
      return sessionLocations;
    }

    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT DISTINCT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM files f JOIN span_locations l ON l.file_id = f.file_id
      JOIN shards s ON s.shard_id = l.shard_id
      WHERE f.project_id = $projectId AND f.relative_path = $relativePath
        AND f.source_hash = $sourceHash AND l.span_start = $spanStart
        AND l.span_length = $spanLength AND s.status = 'Complete' ORDER BY s.shard_id;
      """;
    command.Parameters.AddWithValue("$projectId", lookup.File.ProjectId);
    command.Parameters.AddWithValue("$relativePath", lookup.File.RelativePath);
    command.Parameters.AddWithValue("$sourceHash", lookup.File.SourceHash);
    command.Parameters.AddWithValue("$spanStart", lookup.SpanStart);
    command.Parameters.AddWithValue("$spanLength", lookup.SpanLength);
    return await ReadLocationsAsync(command, cancellationToken);
  }

  public async Task PublishAsync(CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(lease);
    ArgumentNullException.ThrowIfNull(shard);
    if (lease.Lookup != shard.Lookup || lease.Location.Status != CpgShardStatus.Complete)
    {
      throw new InvalidOperationException("Only a complete shard with matching lookup can be published.");
    }

    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
    var fileId = Hash("file", lease.Lookup.File.ProjectId, lease.Lookup.File.RelativePath, lease.Lookup.File.SourceHash);
    var fragmentId = Hash("fragment", fileId, lease.Lookup.Fragment.Kind, lease.Lookup.Fragment.SpanStart.ToString(), lease.Lookup.Fragment.SpanLength.ToString(), lease.Lookup.Fragment.FragmentHash);
    await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO projects(project_id, root_path, schema_version, builder_profile_hash) VALUES($a, $b, $c, $d);", cancellationToken, lease.Lookup.File.ProjectId, lease.Lookup.File.ProjectId, lease.Lookup.SchemaVersion, lease.Lookup.ProfileHash);
    await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO files(file_id, project_id, relative_path, source_hash, status) VALUES($a, $b, $c, $d, 'Complete');", cancellationToken, fileId, lease.Lookup.File.ProjectId, lease.Lookup.File.RelativePath, lease.Lookup.File.SourceHash);
    await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO fragments(fragment_id, file_id, fragment_kind, span_start, span_length, fragment_hash, status) VALUES($a, $b, $c, $d, $e, $f, 'Complete');", cancellationToken, fragmentId, fileId, lease.Lookup.Fragment.Kind, lease.Lookup.Fragment.SpanStart, lease.Lookup.Fragment.SpanLength, lease.Lookup.Fragment.FragmentHash);
    await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO shards(shard_id, fragment_id, shard_path, byte_length, shard_hash, status, completed_at_utc) VALUES($a, $b, $c, $d, $e, 'Complete', $f);", cancellationToken, lease.Location.ShardId, fragmentId, lease.Location.ShardPath, lease.Location.ByteLength, lease.Location.ShardHash, DateTimeOffset.UtcNow.ToString("O"));
    foreach (var node in shard.Nodes)
    {
      await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO node_locations(node_id, shard_id, local_offset) VALUES($a, $b, $c);", cancellationToken, node.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture), lease.Location.ShardId, node.LocalIndex);
      if (node.SpanStart.HasValue && node.SpanEnd.HasValue)
      {
        await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO span_locations(file_id, span_start, span_length, shard_id) VALUES($a, $b, $c, $d);", cancellationToken, fileId, node.SpanStart.Value, node.SpanEnd.Value - node.SpanStart.Value, lease.Location.ShardId);
      }
    }

    foreach (var symbol in shard.SymbolLocations)
    {
      await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO symbol_locations(symbol_key, shard_id, local_offset) VALUES($a, $b, $c);", cancellationToken, symbol.SymbolKey, lease.Location.ShardId, symbol.LocalIndex);
    }

    await transaction.CommitAsync(cancellationToken);
  }

  public async Task<string> BeginBuildAsync(CancellationToken cancellationToken)
  {
    var buildId = Guid.NewGuid().ToString("N");
    await using var connection = await OpenAsync(cancellationToken);
    await ExecuteAsync(
      connection,
      transaction: null,
      "INSERT INTO build_sessions(build_id, status, created_at_utc, completed_at_utc) VALUES($a, 'Building', $b, NULL);",
      cancellationToken,
      buildId,
      DateTimeOffset.UtcNow.ToString("O"));
    return buildId;
  }

  public async Task StageAsync(
    string buildId,
    CpgShardLease lease,
    CpgFrozenShard shard,
    CancellationToken cancellationToken)
  {
    await StageBatchAsync(
      buildId,
      new[] { new CpgCatalogPublication(lease, shard, WriteLegacyRoutingRows: true) },
      cancellationToken);
  }

  public async Task StageReusableAsync(
    string buildId,
    CpgShardLease lease,
    CpgFrozenShard shard,
    CpgReusableFragmentKey reusableKey,
    CancellationToken cancellationToken)
  {
    await StageBatchAsync(
      buildId,
      new[] { new CpgCatalogPublication(lease, shard, reusableKey, WriteLegacyRoutingRows: true) },
      cancellationToken);
  }

  internal async Task CloneReusableAsync(
    string buildId,
    CpgShardLookup targetLookup,
    CpgReusableShardLease source,
    CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
    ArgumentNullException.ThrowIfNull(targetLookup);
    ArgumentNullException.ThrowIfNull(source);
    ValidateReusableKey(source.Key, targetLookup);

    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
    await EnsureBuildingAsync(connection, transaction, buildId, cancellationToken);
    await CloneReusableAsync(
      connection,
      transaction,
      buildId,
      targetLookup,
      source,
      writeLegacyRoutingRows: true,
      cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  private static async Task CloneReusableAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string buildId,
    CpgShardLookup targetLookup,
    CpgReusableShardLease source,
    bool writeLegacyRoutingRows,
    CancellationToken cancellationToken)
  {
    ValidateReusableKey(source.Key, targetLookup);
    var fileId = Hash(
      "session-file",
      buildId,
      targetLookup.File.ProjectId,
      targetLookup.File.RelativePath,
      targetLookup.File.SourceHash);
    var fragmentId = Hash(
      "session-fragment",
      buildId,
      fileId,
      targetLookup.Fragment.Kind,
      targetLookup.Fragment.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture),
      targetLookup.Fragment.SpanLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
      targetLookup.Fragment.FragmentHash);
    await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO session_files(build_id, file_id, project_id, relative_path, source_hash, schema_version, builder_profile_hash) VALUES($a, $b, $c, $d, $e, $f, $g);", cancellationToken, buildId, fileId, targetLookup.File.ProjectId, targetLookup.File.RelativePath, targetLookup.File.SourceHash, targetLookup.SchemaVersion, targetLookup.ProfileHash);
    await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO session_fragments(build_id, fragment_id, file_id, fragment_kind, span_start, span_length, fragment_hash) VALUES($a, $b, $c, $d, $e, $f, $g);", cancellationToken, buildId, fragmentId, fileId, targetLookup.Fragment.Kind, targetLookup.Fragment.SpanStart, targetLookup.Fragment.SpanLength, targetLookup.Fragment.FragmentHash);
    await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO session_shards(build_id, shard_id, fragment_id, shard_path, byte_length, shard_hash) VALUES($a, $b, $c, $d, $e, $f);", cancellationToken, buildId, source.Location.ShardId, fragmentId, source.Location.ShardPath, source.Location.ByteLength, source.Location.ShardHash);
    await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO session_reusable_fragments(build_id, project_id, relative_path, schema_version, builder_profile_hash, fragment_kind, span_start, span_length, fragment_hash, node_id_fingerprint, shard_id) VALUES($a, $b, $c, $d, $e, $f, $g, $h, $i, $j, $k);", cancellationToken, buildId, source.Key.ProjectId, source.Key.RelativePath, source.Key.SchemaVersion, source.Key.ProfileHash, source.Key.FragmentKind, source.Key.SpanStart, source.Key.SpanLength, source.Key.FragmentHash, source.Key.NodeIdFingerprint, source.Location.ShardId);
    await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO session_fragment_owners(build_id, shard_id, owner_fragment_id) VALUES($a, $b, $c);", cancellationToken, buildId, source.Location.ShardId, CpgFragmentOwnerIdentity.Create(targetLookup));
    if (writeLegacyRoutingRows)
    {
      await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO session_node_locations(build_id, node_id, shard_id, local_offset) SELECT $a, node_id, shard_id, local_offset FROM session_node_locations WHERE build_id = $b AND shard_id = $c;", cancellationToken, buildId, source.SourceBuildId, source.Location.ShardId);
      await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO session_span_locations(build_id, file_id, span_start, span_length, shard_id) SELECT $a, $b, span_start, span_length, shard_id FROM session_span_locations WHERE build_id = $c AND shard_id = $d;", cancellationToken, buildId, fileId, source.SourceBuildId, source.Location.ShardId);
      await ExecuteAsync(connection, transaction, "INSERT OR REPLACE INTO session_symbol_locations(build_id, symbol_key, shard_id, local_offset) SELECT $a, symbol_key, shard_id, local_offset FROM session_symbol_locations WHERE build_id = $b AND shard_id = $c;", cancellationToken, buildId, source.SourceBuildId, source.Location.ShardId);
      await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO session_boundary_node_locations(build_id, node_id, shard_id) SELECT $a, node_id, shard_id FROM session_boundary_node_locations WHERE build_id = $b AND shard_id = $c;", cancellationToken, buildId, source.SourceBuildId, source.Location.ShardId);
    }
  }

  internal async Task FinalizeBuildAsync(
    string buildId,
    IReadOnlyList<Builder.CpgReusableCloneRequest> reusableCloneRequests,
    CpgBuildRoutingIndexManifest? routingIndexManifest,
    CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
    ArgumentNullException.ThrowIfNull(reusableCloneRequests);
    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
    await EnsureBuildingAsync(connection, transaction, buildId, cancellationToken);
    foreach (var request in reusableCloneRequests)
    {
      await CloneReusableAsync(
        connection,
        transaction,
        buildId,
        request.TargetLookup,
        request.Source,
        writeLegacyRoutingRows: routingIndexManifest is null,
        cancellationToken);
    }

    if (routingIndexManifest is not null)
    {
      await WriteRoutingIndexManifestAsync(
        connection,
        transaction,
        buildId,
        routingIndexManifest,
        cancellationToken);
    }

    await CompleteBuildAsync(connection, transaction, buildId, cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  internal async Task StageBatchAsync(
    string buildId,
    IReadOnlyList<CpgCatalogPublication> publications,
    CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
    ArgumentNullException.ThrowIfNull(publications);
    if (publications.Count == 0)
    {
      return;
    }

    await using var connection = await OpenAsync(cancellationToken);
    _ = await StageBatchAsync(connection, buildId, publications, commandCache: null, cancellationToken);
  }

  internal Task<SqliteConnection> OpenBatchConnectionAsync(CancellationToken cancellationToken)
  {
    return OpenAsync(cancellationToken);
  }

  internal async Task<CpgCatalogStageTelemetry> StageBatchAsync(
    SqliteConnection connection,
    string buildId,
    IReadOnlyList<CpgCatalogPublication> publications,
    SqliteCpgCatalogCommandCache? commandCache,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(connection);
    ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
    ArgumentNullException.ThrowIfNull(publications);
    if (publications.Count == 0)
    {
      return new CpgCatalogStageTelemetry();
    }

    var telemetry = new CpgCatalogStageTelemetry();
    var stopwatch = Stopwatch.StartNew();
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
    telemetry.TransactionBeginMilliseconds += stopwatch.ElapsedMilliseconds;
    stopwatch.Restart();
    await EnsureBuildingAsync(connection, transaction, buildId, cancellationToken);
    telemetry.EnsureBuildingMilliseconds += stopwatch.ElapsedMilliseconds;
    telemetry.RecordStatement(affectedRows: 0);
    foreach (var publication in publications)
    {
      await StageOneAsync(connection, transaction, buildId, publication, commandCache, telemetry, cancellationToken);
    }

    stopwatch.Restart();
    await transaction.CommitAsync(cancellationToken);
    telemetry.TransactionCommitMilliseconds += stopwatch.ElapsedMilliseconds;
    return telemetry;
  }

  private static async Task StageOneAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string buildId,
    CpgCatalogPublication publication,
    SqliteCpgCatalogCommandCache? commandCache,
    CpgCatalogStageTelemetry telemetry,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(publication);
    var lease = publication.Lease;
    var shard = publication.Shard;
    if (lease.Lookup != shard.Lookup || lease.Location.Status != CpgShardStatus.Complete)
    {
      throw new InvalidOperationException("Only a complete shard with matching lookup can be staged.");
    }

    var fixedMetadataStopwatch = Stopwatch.StartNew();
    var fileId = Hash("session-file", buildId, lease.Lookup.File.ProjectId, lease.Lookup.File.RelativePath, lease.Lookup.File.SourceHash);
    var fragmentId = Hash(
      "session-fragment",
      buildId,
      fileId,
      lease.Lookup.Fragment.Kind,
      lease.Lookup.Fragment.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture),
      lease.Lookup.Fragment.SpanLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
      lease.Lookup.Fragment.FragmentHash);
    await ExecuteStageAsync(commandCache, connection, transaction, telemetry, "INSERT OR IGNORE INTO session_files(build_id, file_id, project_id, relative_path, source_hash, schema_version, builder_profile_hash) VALUES($a, $b, $c, $d, $e, $f, $g);", cancellationToken, buildId, fileId, lease.Lookup.File.ProjectId, lease.Lookup.File.RelativePath, lease.Lookup.File.SourceHash, lease.Lookup.SchemaVersion, lease.Lookup.ProfileHash);
    await ExecuteStageAsync(commandCache, connection, transaction, telemetry, "INSERT OR REPLACE INTO session_fragments(build_id, fragment_id, file_id, fragment_kind, span_start, span_length, fragment_hash) VALUES($a, $b, $c, $d, $e, $f, $g);", cancellationToken, buildId, fragmentId, fileId, lease.Lookup.Fragment.Kind, lease.Lookup.Fragment.SpanStart, lease.Lookup.Fragment.SpanLength, lease.Lookup.Fragment.FragmentHash);
    await ExecuteStageAsync(commandCache, connection, transaction, telemetry, "INSERT OR REPLACE INTO session_shards(build_id, shard_id, fragment_id, shard_path, byte_length, shard_hash) VALUES($a, $b, $c, $d, $e, $f);", cancellationToken, buildId, lease.Location.ShardId, fragmentId, lease.Location.ShardPath, lease.Location.ByteLength, lease.Location.ShardHash);
    if (publication.ReusableKey is { } reusableKey)
    {
      ValidateReusableKey(reusableKey, lease.Lookup);
      await ExecuteStageAsync(commandCache, connection, transaction, telemetry, "INSERT OR REPLACE INTO session_reusable_fragments(build_id, project_id, relative_path, schema_version, builder_profile_hash, fragment_kind, span_start, span_length, fragment_hash, node_id_fingerprint, shard_id) VALUES($a, $b, $c, $d, $e, $f, $g, $h, $i, $j, $k);", cancellationToken, buildId, reusableKey.ProjectId, reusableKey.RelativePath, reusableKey.SchemaVersion, reusableKey.ProfileHash, reusableKey.FragmentKind, reusableKey.SpanStart, reusableKey.SpanLength, reusableKey.FragmentHash, reusableKey.NodeIdFingerprint, lease.Location.ShardId);
    }
    if (shard.Role == CpgShardRole.Primary)
    {
      await ExecuteStageAsync(commandCache,
        connection,
        transaction,
        telemetry,
        "INSERT OR REPLACE INTO session_fragment_owners(build_id, shard_id, owner_fragment_id) VALUES($a, $b, $c);",
        cancellationToken,
        buildId,
        lease.Location.ShardId,
        CpgFragmentOwnerIdentity.Create(lease.Lookup));
    }
    else
    {
      if (shard.BoundaryAdjacency is null || shard.Nodes.Count != 0 || shard.Edges.Count != 0)
      {
        throw new InvalidOperationException("A boundary-adjacency shard must have an owner and contain only boundary edges.");
      }

      await ExecuteStageAsync(commandCache,
        connection,
        transaction,
        telemetry,
        "INSERT INTO session_fragment_adjacencies(build_id, owner_fragment_id, shard_id, direction) VALUES($a, $b, $c, $d);",
        cancellationToken,
        buildId,
        shard.BoundaryAdjacency.OwnerFragmentId,
        lease.Location.ShardId,
        shard.BoundaryAdjacency.Direction.ToString().ToLowerInvariant());
      fixedMetadataStopwatch.Stop();
      telemetry.FixedMetadataMilliseconds += fixedMetadataStopwatch.ElapsedMilliseconds;
      if (publication.WriteLegacyRoutingRows)
      {
        var boundaryStopwatch = Stopwatch.StartNew();
        var boundaryMaterializationStopwatch = Stopwatch.StartNew();
        var boundaryMaterializationAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        var boundaryNodeRows = shard.BoundaryEdges!
          .SelectMany(edge => new[] { edge.SourceNodeId, edge.TargetNodeId })
          .Distinct()
          .Select(nodeId => new object?[]
          {
            buildId,
            nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            lease.Location.ShardId,
          })
          .ToArray();
        boundaryMaterializationStopwatch.Stop();
        telemetry.RecordRowMaterialization(
          boundaryMaterializationStopwatch.ElapsedMilliseconds,
          boundaryMaterializationAllocatedBytes);
        await ExecuteStageRowsAsync(
          commandCache,
          connection,
          transaction,
          "session_boundary_node_locations",
          "build_id, node_id, shard_id",
          "OR IGNORE",
          boundaryNodeRows,
          telemetry,
          cancellationToken);
        boundaryStopwatch.Stop();
        telemetry.BoundaryWriteMilliseconds += boundaryStopwatch.ElapsedMilliseconds;
      }
    }
    if (shard.Role == CpgShardRole.Primary)
    {
      fixedMetadataStopwatch.Stop();
      telemetry.FixedMetadataMilliseconds += fixedMetadataStopwatch.ElapsedMilliseconds;
    }
    if (!publication.WriteLegacyRoutingRows)
    {
      return;
    }

    var nodeStopwatch = Stopwatch.StartNew();
    var rowMaterializationStopwatch = Stopwatch.StartNew();
    var rowMaterializationAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    var nodeRows = shard.Nodes.Select(node => new object?[]
    {
      buildId,
      node.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
      lease.Location.ShardId,
      node.LocalIndex,
    }).ToArray();
    rowMaterializationStopwatch.Stop();
    telemetry.RecordRowMaterialization(
      rowMaterializationStopwatch.ElapsedMilliseconds,
      rowMaterializationAllocatedBytes);
    await ExecuteStageRowsAsync(
      commandCache,
      connection,
      transaction,
      "session_node_locations",
      "build_id, node_id, shard_id, local_offset",
      "OR REPLACE",
      nodeRows,
      telemetry,
      cancellationToken);
    nodeStopwatch.Stop();
    telemetry.NodeWriteMilliseconds += nodeStopwatch.ElapsedMilliseconds;
    var spanStopwatch = Stopwatch.StartNew();
    rowMaterializationStopwatch = Stopwatch.StartNew();
    rowMaterializationAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    var spanRows = shard.Nodes
      .Where(node => node.SpanStart.HasValue && node.SpanEnd.HasValue)
      .Select(node => new object?[]
      {
        buildId,
        fileId,
        node.SpanStart!.Value,
        node.SpanEnd!.Value - node.SpanStart.Value,
        lease.Location.ShardId,
      })
      .ToArray();
    rowMaterializationStopwatch.Stop();
    telemetry.RecordRowMaterialization(
      rowMaterializationStopwatch.ElapsedMilliseconds,
      rowMaterializationAllocatedBytes);
    await ExecuteStageRowsAsync(
      commandCache,
      connection,
      transaction,
      "session_span_locations",
      "build_id, file_id, span_start, span_length, shard_id",
      "OR REPLACE",
      spanRows,
      telemetry,
      cancellationToken);
    spanStopwatch.Stop();
    telemetry.SpanWriteMilliseconds += spanStopwatch.ElapsedMilliseconds;
    var symbolStopwatch = Stopwatch.StartNew();
    rowMaterializationStopwatch = Stopwatch.StartNew();
    rowMaterializationAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    var symbolRows = shard.SymbolLocations.Select(symbol => new object?[]
    {
      buildId,
      symbol.SymbolKey,
      lease.Location.ShardId,
      symbol.LocalIndex,
    }).ToArray();
    rowMaterializationStopwatch.Stop();
    telemetry.RecordRowMaterialization(
      rowMaterializationStopwatch.ElapsedMilliseconds,
      rowMaterializationAllocatedBytes);
    await ExecuteStageRowsAsync(
      commandCache,
      connection,
      transaction,
      "session_symbol_locations",
      "build_id, symbol_key, shard_id, local_offset",
      "OR REPLACE",
      symbolRows,
      telemetry,
      cancellationToken);
    symbolStopwatch.Stop();
    telemetry.SymbolWriteMilliseconds += symbolStopwatch.ElapsedMilliseconds;

  }

  private static async Task ExecuteStageAsync(
    SqliteCpgCatalogCommandCache? commandCache,
    SqliteConnection connection,
    SqliteTransaction transaction,
    CpgCatalogStageTelemetry telemetry,
    string sql,
    CancellationToken cancellationToken,
    params object?[] values)
  {
    var affectedRows = commandCache is null
      ? await ExecuteAsync(connection, transaction, sql, telemetry, cancellationToken, values)
      : await commandCache.ExecuteAsync(transaction, sql, telemetry, cancellationToken, values);
    telemetry.RecordStatement(rows: 1, affectedRows);
  }

  private static async Task ExecuteStageRowsAsync(
    SqliteCpgCatalogCommandCache? commandCache,
    SqliteConnection connection,
    SqliteTransaction transaction,
    string tableName,
    string columnNames,
    string conflictClause,
    IReadOnlyList<object?[]> rows,
    CpgCatalogStageTelemetry telemetry,
    CancellationToken cancellationToken)
  {
    if (rows.Count == 0)
    {
      return;
    }

    const int maxParametersPerStatement = 900;
    var valuesPerRow = rows[0].Length;
    var maxRowsPerStatement = Math.Max(1, maxParametersPerStatement / valuesPerRow);
    for (var start = 0; start < rows.Count; start += maxRowsPerStatement)
    {
      var count = Math.Min(maxRowsPerStatement, rows.Count - start);
      var sqlTextBuildStopwatch = Stopwatch.StartNew();
      var sqlTextBuildAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
      var values = new List<object?>(count * valuesPerRow);
      var valueGroups = new string[count];
      for (var rowIndex = 0; rowIndex < count; rowIndex += 1)
      {
        var parameterNames = new string[valuesPerRow];
        for (var valueIndex = 0; valueIndex < valuesPerRow; valueIndex += 1)
        {
          var parameterIndex = (rowIndex * valuesPerRow) + valueIndex;
          parameterNames[valueIndex] = $"$p{parameterIndex}";
          values.Add(rows[start + rowIndex][valueIndex]);
        }

        valueGroups[rowIndex] = $"({string.Join(", ", parameterNames)})";
      }

      var sql = $"INSERT {conflictClause} INTO {tableName}({columnNames}) VALUES {string.Join(", ", valueGroups)};";
      sqlTextBuildStopwatch.Stop();
      telemetry.RecordSqlTextBuild(
        sqlTextBuildStopwatch.ElapsedMilliseconds,
        sqlTextBuildAllocatedBytes);
      var affectedRows = commandCache is null
        ? await ExecuteValuesAsync(connection, transaction, sql, values, telemetry, cancellationToken)
        : await commandCache.ExecuteValuesAsync(transaction, sql, values, telemetry, cancellationToken);
      telemetry.RecordStatement(count, affectedRows);
    }
  }

  private static async Task<int> ExecuteValuesAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string sql,
    IReadOnlyList<object?> values,
    CpgCatalogStageTelemetry telemetry,
    CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    for (var index = 0; index < values.Count; index += 1)
    {
      command.Parameters.AddWithValue($"$p{index}", values[index] ?? DBNull.Value);
    }

    var executeStopwatch = Stopwatch.StartNew();
    var executeAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
    executeStopwatch.Stop();
    telemetry.RecordExecuteNonQuery(executeStopwatch.ElapsedMilliseconds, executeAllocatedBytes);
    return affectedRows;
  }

  private static readonly string[] SessionTables =
  {
    "session_files",
    "session_fragments",
    "session_shards",
    "session_node_locations",
    "session_fragment_owners",
    "session_fragment_adjacencies",
    "session_boundary_node_locations",
    "session_symbol_locations",
    "session_span_locations",
    "session_reusable_fragments",
    "session_manifests",
    "session_routing_indexes",
  };

  private static async Task WriteRoutingIndexManifestAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string buildId,
    CpgBuildRoutingIndexManifest manifest,
    CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(manifest.RelativePath);
    ArgumentException.ThrowIfNullOrWhiteSpace(manifest.PayloadHash);
    if (manifest.FormatVersion <= 0 || manifest.ByteLength <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(manifest));
    }

    await ExecuteAsync(
      connection,
      transaction,
      """
      INSERT INTO session_routing_indexes(build_id, relative_path, format_version, byte_length, payload_hash)
      VALUES($a, $b, $c, $d, $e);
      """,
      cancellationToken,
      buildId,
      manifest.RelativePath,
      manifest.FormatVersion,
      manifest.ByteLength,
      manifest.PayloadHash);
  }

  private static async Task CompleteBuildAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string buildId,
    CancellationToken cancellationToken)
  {
    var completedAt = DateTimeOffset.UtcNow.ToString("O");
    await using var complete = connection.CreateCommand();
    complete.Transaction = transaction;
    complete.CommandText = "UPDATE build_sessions SET status = 'Complete', completed_at_utc = $completedAt WHERE build_id = $buildId AND status = 'Building';";
    complete.Parameters.AddWithValue("$completedAt", completedAt);
    complete.Parameters.AddWithValue("$buildId", buildId);
    if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
    {
      throw new InvalidOperationException("Only an active CPG build session can be completed.");
    }

    await ExecuteAsync(
      connection,
      transaction,
      """
      INSERT OR REPLACE INTO session_manifests(build_id, completed_at_utc, shard_count, shard_bytes)
      SELECT $a, $b, COUNT(*), COALESCE(SUM(byte_length), 0)
      FROM session_shards WHERE build_id = $a;
      """,
      cancellationToken,
      buildId,
      completedAt);
    await ExecuteAsync(
      connection,
      transaction,
      """
      INSERT INTO physical_shard_references(shard_path, shard_hash, byte_length, reference_count)
      SELECT shard_path, shard_hash, byte_length, COUNT(*)
      FROM session_shards
      WHERE build_id = $a
      GROUP BY shard_path, shard_hash, byte_length
      ON CONFLICT(shard_path) DO UPDATE SET
        shard_hash = excluded.shard_hash,
        byte_length = excluded.byte_length,
        reference_count = physical_shard_references.reference_count + excluded.reference_count;
      """,
      cancellationToken,
      buildId);
  }

  private static async Task DecrementPhysicalShardReferencesAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    IReadOnlyList<string> buildsToPrune,
    CancellationToken cancellationToken)
  {
    await using var decrement = connection.CreateCommand();
    decrement.Transaction = transaction;
    var buildParameters = CreateParameterList(decrement, buildsToPrune);
    decrement.CommandText = $"""
      UPDATE physical_shard_references
      SET reference_count = reference_count - (
        SELECT COUNT(*) FROM session_shards
        WHERE build_id IN ({buildParameters})
          AND shard_path = physical_shard_references.shard_path)
      WHERE shard_path IN (
        SELECT DISTINCT shard_path FROM session_shards
        WHERE build_id IN ({buildParameters}));
      """;
    await decrement.ExecuteNonQueryAsync(cancellationToken);
    await ExecuteAsync(
      connection,
      transaction,
      "DELETE FROM physical_shard_references WHERE reference_count <= 0;",
      cancellationToken);
  }

  private static string CreateParameterList(SqliteCommand command, IReadOnlyList<string> values)
  {
    var parameters = new string[values.Count];
    for (var index = 0; index < values.Count; index += 1)
    {
      var parameterName = $"$build{index}";
      parameters[index] = parameterName;
      command.Parameters.AddWithValue(parameterName, values[index]);
    }

    return string.Join(", ", parameters);
  }

  public async Task CompleteBuildAsync(string buildId, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
    await CompleteBuildAsync(connection, transaction, buildId, cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  public async Task<CpgCatalogMaintenanceResult> PruneCompletedBuildsAsync(
    int maxCompletedBuilds,
    CancellationToken cancellationToken)
  {
    if (maxCompletedBuilds < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(maxCompletedBuilds));
    }

    using var storeLock = CpgShardStoreLock.Acquire(_storeRoot);
    await using var connection = await OpenAsync(cancellationToken);
    var buildsToPrune = new List<string>();
    await using (var select = connection.CreateCommand())
    {
      select.CommandText = """
        SELECT build_id FROM build_sessions WHERE status = 'Complete'
        ORDER BY completed_at_utc DESC, build_id DESC LIMIT -1 OFFSET $retainCount;
        """;
      select.Parameters.AddWithValue("$retainCount", maxCompletedBuilds);
      await using var reader = await select.ExecuteReaderAsync(cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        buildsToPrune.Add(reader.GetString(0));
      }
    }

    if (buildsToPrune.Count == 0)
    {
      return new CpgCatalogMaintenanceResult(0, 0, 0);
    }

    var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var paths = connection.CreateCommand())
    {
      paths.CommandText = $"SELECT DISTINCT shard_path FROM session_shards WHERE build_id IN ({CreateParameterList(paths, buildsToPrune)});";
      await using var reader = await paths.ExecuteReaderAsync(cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        candidatePaths.Add(reader.GetString(0));
      }
    }

    await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken))
    {
      await DecrementPhysicalShardReferencesAsync(connection, transaction, buildsToPrune, cancellationToken);
      foreach (var table in SessionTables)
      {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = $"DELETE FROM {table} WHERE build_id IN ({CreateParameterList(delete, buildsToPrune)});";
        await delete.ExecuteNonQueryAsync(cancellationToken);
      }

      await using var deleteBuilds = connection.CreateCommand();
      deleteBuilds.Transaction = transaction;
      deleteBuilds.CommandText = $"DELETE FROM build_sessions WHERE build_id IN ({CreateParameterList(deleteBuilds, buildsToPrune)});";
      await deleteBuilds.ExecuteNonQueryAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    }

    var deletedShards = 0;
    foreach (var path in candidatePaths)
    {
      await using var reference = connection.CreateCommand();
      reference.CommandText = "SELECT COUNT(*) FROM physical_shard_references WHERE shard_path = $path;";
      reference.Parameters.AddWithValue("$path", path);
      if (Convert.ToInt64(await reference.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 0 &&
          File.Exists(path))
      {
        File.Delete(path);
        deletedShards += 1;
      }
    }

    return new CpgCatalogMaintenanceResult(buildsToPrune.Count, candidatePaths.Count, deletedShards);
  }

  public async Task InvalidateBuildAsync(string buildId, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "UPDATE build_sessions SET status = 'Invalid' WHERE build_id = $buildId AND status = 'Building';";
    command.Parameters.AddWithValue("$buildId", buildId);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task InvalidateFileAsync(CpgFileKey fileKey, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var invalidateSessions = connection.CreateCommand();
    invalidateSessions.CommandText = """
      UPDATE build_sessions SET status = 'Invalid'
      WHERE build_id IN (
        SELECT build_id FROM session_files
        WHERE project_id = $projectId AND relative_path = $relativePath AND source_hash = $sourceHash);
      """;
    invalidateSessions.Parameters.AddWithValue("$projectId", fileKey.ProjectId);
    invalidateSessions.Parameters.AddWithValue("$relativePath", fileKey.RelativePath);
    invalidateSessions.Parameters.AddWithValue("$sourceHash", fileKey.SourceHash);
    await invalidateSessions.ExecuteNonQueryAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "UPDATE files SET status = 'Invalid' WHERE project_id = $projectId AND relative_path = $relativePath AND source_hash = $sourceHash;";
    command.Parameters.AddWithValue("$projectId", fileKey.ProjectId);
    command.Parameters.AddWithValue("$relativePath", fileKey.RelativePath);
    command.Parameters.AddWithValue("$sourceHash", fileKey.SourceHash);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task<int> RebuildFromShardHeadersAsync(string storeRoot, CancellationToken cancellationToken)
  {
    var store = new CpgShardStore(storeRoot);
    var rebuiltCount = 0;
    foreach (var shardsRoot in EnumerateRecoverableShardsRoots(storeRoot))
    {
      foreach (var path in Directory.EnumerateFiles(shardsRoot, "*.cpgbin", SearchOption.AllDirectories))
      {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
          var (shard, location) = await store.ReadFromPathAsync(path, cancellationToken);
          await PublishAsync(new CpgShardLease(shard.Lookup, location), shard, cancellationToken);
          rebuiltCount += 1;
        }
        catch (InvalidDataException)
        {
        }
      }
    }

    return rebuiltCount;
  }

  private static IEnumerable<string> EnumerateRecoverableShardsRoots(string storeRoot)
  {
    var rootShards = Path.Combine(storeRoot, "shards");
    if (Directory.Exists(rootShards))
    {
      yield return rootShards;
    }

    var buildsRoot = Path.Combine(storeRoot, "builds");
    if (!Directory.Exists(buildsRoot))
    {
      yield break;
    }

    foreach (var buildRoot in Directory.EnumerateDirectories(buildsRoot))
    {
      if (!File.Exists(Path.Combine(buildRoot, CompletionMarkerFileName)))
      {
        continue;
      }

      var shardsRoot = Path.Combine(buildRoot, "shards");
      if (Directory.Exists(shardsRoot))
      {
        yield return shardsRoot;
      }
    }
  }

  private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
  {
    var connection = new SqliteConnection(_connectionString);
    try
    {
      await connection.OpenAsync(cancellationToken);
      if (Volatile.Read(ref _schemaInitialized) == 0)
      {
        await _schemaInitializationGate.WaitAsync(cancellationToken);
        try
        {
          if (Volatile.Read(ref _schemaInitialized) == 0)
          {
            await SqliteCpgShardSchema.EnsureCreatedAsync(connection, cancellationToken);
            Volatile.Write(ref _schemaInitialized, 1);
          }
        }
        finally
        {
          _schemaInitializationGate.Release();
        }
      }

      return connection;
    }
    catch
    {
      await connection.DisposeAsync();
      throw;
    }
  }

  private async Task<CpgShardLease?> TryAcquireSessionAsync(CpgShardLookup lookup, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM build_sessions b
      JOIN session_files f ON f.build_id = b.build_id
      JOIN session_fragments g ON g.build_id = f.build_id AND g.file_id = f.file_id
      JOIN session_shards s ON s.build_id = g.build_id AND s.fragment_id = g.fragment_id
      WHERE b.status = 'Complete' AND f.project_id = $projectId
        AND f.relative_path = $relativePath AND f.source_hash = $sourceHash
        AND f.schema_version = $schemaVersion AND f.builder_profile_hash = $profileHash
        AND g.fragment_kind = $fragmentKind AND g.span_start = $spanStart
        AND g.span_length = $spanLength AND g.fragment_hash = $fragmentHash
      ORDER BY b.completed_at_utc DESC, b.build_id DESC, s.shard_id LIMIT 1;
      """;
    BindLookup(command, lookup);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }

    var location = new CpgShardLocation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), CpgShardStatus.Complete);
    return new CpgShardLease(lookup, location);
  }

  private async Task<IReadOnlyList<CpgShardLocation>> FindSessionByFileAsync(CpgFileKey fileKey, int schemaVersion, string profileHash, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      WITH latest_build AS (
        SELECT b.build_id
        FROM build_sessions b JOIN session_files f ON f.build_id = b.build_id
        WHERE b.status = 'Complete' AND f.project_id = $projectId
          AND f.relative_path = $relativePath AND f.source_hash = $sourceHash
          AND f.schema_version = $schemaVersion AND f.builder_profile_hash = $profileHash
        ORDER BY b.completed_at_utc DESC, b.build_id DESC LIMIT 1)
      SELECT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM latest_build b
      JOIN session_fragments g ON g.build_id = b.build_id
      JOIN session_shards s ON s.build_id = g.build_id AND s.fragment_id = g.fragment_id
      ORDER BY s.shard_id;
      """;
    command.Parameters.AddWithValue("$projectId", fileKey.ProjectId);
    command.Parameters.AddWithValue("$relativePath", fileKey.RelativePath);
    command.Parameters.AddWithValue("$sourceHash", fileKey.SourceHash);
    command.Parameters.AddWithValue("$schemaVersion", schemaVersion);
    command.Parameters.AddWithValue("$profileHash", profileHash);
    return await ReadLocationsAsync(command, cancellationToken);
  }

  private async Task<IReadOnlyList<CpgShardLocation>?> FindByNodeRoutingIndexAsync(
    uint nodeId,
    CancellationToken cancellationToken)
  {
    foreach (var candidate in await ReadRoutingIndexCandidatesAsync(cancellationToken))
    {
      var primaryRoutes = candidate.Index.FindPrimaryNode(nodeId);
      var boundaryRoutes = candidate.Index.FindBoundaryNode(nodeId);
      if (primaryRoutes.Count == 0 && boundaryRoutes.Count == 0)
      {
        continue;
      }

      var shardIds = new HashSet<string>(
        primaryRoutes.Select(route => route.ShardId),
        StringComparer.Ordinal);
      foreach (var shardId in boundaryRoutes.Select(route => route.ShardId))
      {
        shardIds.Add(shardId);
      }

      if (primaryRoutes.Count > 0)
      {
        foreach (var shardId in await ReadAdjacentShardIdsAsync(
          candidate.BuildId,
          primaryRoutes.Select(route => route.ShardId),
          cancellationToken))
        {
          shardIds.Add(shardId);
        }
      }

      return await ReadSessionLocationsByShardIdsAsync(candidate.BuildId, shardIds, cancellationToken);
    }

    return null;
  }

  private async Task<IReadOnlyList<CpgShardLocation>?> FindBySymbolRoutingIndexAsync(
    CpgSymbolLookup lookup,
    CancellationToken cancellationToken)
  {
    foreach (var candidate in await ReadRoutingIndexCandidatesAsync(cancellationToken))
    {
      var shardIds = candidate.Index.FindBySymbol(lookup)
        .Select(route => route.ShardId)
        .ToArray();
      if (shardIds.Length > 0)
      {
        return await ReadSessionLocationsByShardIdsAsync(candidate.BuildId, shardIds, cancellationToken);
      }
    }

    return null;
  }

  private async Task<IReadOnlyList<CpgShardLocation>?> FindBySpanRoutingIndexAsync(
    CpgSpanLookup lookup,
    CancellationToken cancellationToken)
  {
    foreach (var candidate in await ReadRoutingIndexCandidatesAsync(cancellationToken))
    {
      var shardIds = candidate.Index.FindBySpan(lookup)
        .Select(route => route.ShardId)
        .ToArray();
      if (shardIds.Length > 0)
      {
        return await ReadSessionLocationsByShardIdsAsync(candidate.BuildId, shardIds, cancellationToken);
      }
    }

    return null;
  }

  private async Task<IReadOnlyList<CpgRoutingIndexCandidate>> ReadRoutingIndexCandidatesAsync(
    CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT b.build_id, r.relative_path, r.format_version, r.byte_length, r.payload_hash
      FROM build_sessions b
      JOIN session_routing_indexes r ON r.build_id = b.build_id
      WHERE b.status = 'Complete'
      ORDER BY b.completed_at_utc DESC, b.build_id DESC;
      """;
    var candidates = new List<CpgRoutingIndexCandidate>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var buildId = reader.GetString(0);
      var relativePath = reader.GetString(1);
      var formatVersion = reader.GetInt32(2);
      var byteLength = reader.GetInt64(3);
      var payloadHash = reader.GetString(4);
      var path = ResolveRoutingIndexPath(relativePath);
      if (!File.Exists(path) || new FileInfo(path).Length != byteLength)
      {
        throw new InvalidDataException("A completed CPG build is missing its routing index.");
      }

      var index = await new CpgBuildRoutingIndexReader().ReadAsync(path, cancellationToken);
      if (formatVersion != CpgBuildRoutingIndexWriter.FormatVersion ||
          !string.Equals(index.BuildId, buildId, StringComparison.Ordinal) ||
          !string.Equals(index.PayloadHash, payloadHash, StringComparison.Ordinal))
      {
        throw new InvalidDataException("The completed CPG build routing index manifest does not match its file.");
      }

      candidates.Add(new CpgRoutingIndexCandidate(buildId, index));
    }

    return candidates;
  }

  private string ResolveRoutingIndexPath(string relativePath)
  {
    if (Path.IsPathRooted(relativePath))
    {
      throw new InvalidDataException("The CPG routing index manifest path must be relative.");
    }

    var root = Path.GetFullPath(_storeRoot);
    var path = Path.GetFullPath(Path.Combine(root, relativePath));
    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidDataException("The CPG routing index manifest path escapes the store root.");
    }

    return path;
  }

  private async Task<IReadOnlyList<string>> ReadAdjacentShardIdsAsync(
    string buildId,
    IEnumerable<string> primaryShardIds,
    CancellationToken cancellationToken)
  {
    var shardIds = primaryShardIds.Distinct(StringComparer.Ordinal).ToArray();
    if (shardIds.Length == 0)
    {
      return Array.Empty<string>();
    }

    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    var primaryParameters = AddShardIdParameters(command, shardIds);
    command.CommandText = $"""
      SELECT DISTINCT a.shard_id
      FROM session_fragment_owners o
      JOIN session_fragment_adjacencies a
        ON a.build_id = o.build_id AND a.owner_fragment_id = o.owner_fragment_id
      WHERE o.build_id = $buildId AND o.shard_id IN ({primaryParameters});
      """;
    command.Parameters.AddWithValue("$buildId", buildId);
    var results = new List<string>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      results.Add(reader.GetString(0));
    }

    return results;
  }

  private async Task<IReadOnlyList<CpgShardLocation>> ReadSessionLocationsByShardIdsAsync(
    string buildId,
    IEnumerable<string> shardIds,
    CancellationToken cancellationToken)
  {
    var distinctShardIds = shardIds.Distinct(StringComparer.Ordinal).ToArray();
    if (distinctShardIds.Length == 0)
    {
      return Array.Empty<CpgShardLocation>();
    }

    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    var parameters = AddShardIdParameters(command, distinctShardIds);
    command.CommandText = $"""
      SELECT shard_id, shard_path, shard_hash, byte_length
      FROM session_shards
      WHERE build_id = $buildId AND shard_id IN ({parameters})
      ORDER BY shard_id;
      """;
    command.Parameters.AddWithValue("$buildId", buildId);
    return await ReadLocationsAsync(command, cancellationToken);
  }

  private static string AddShardIdParameters(SqliteCommand command, IReadOnlyList<string> shardIds)
  {
    var parameterNames = new string[shardIds.Count];
    for (var index = 0; index < shardIds.Count; index += 1)
    {
      parameterNames[index] = $"$shard{index}";
      command.Parameters.AddWithValue(parameterNames[index], shardIds[index]);
    }

    return string.Join(", ", parameterNames);
  }

  private sealed record CpgRoutingIndexCandidate(string BuildId, CpgBuildRoutingIndex Index);

  private async Task<IReadOnlyList<CpgShardLocation>> FindSessionByNodeAsync(uint nodeId, CancellationToken cancellationToken)
  {
    return await FindSessionLocationsAsync("""
      WITH latest_build AS (
        SELECT b.build_id
        FROM build_sessions b
        WHERE b.status = 'Complete' AND b.build_id IN (
          SELECT build_id FROM session_node_locations WHERE node_id = $value
          UNION
          SELECT build_id FROM session_boundary_node_locations WHERE node_id = $value)
        ORDER BY b.completed_at_utc DESC, b.build_id DESC LIMIT 1),
      primary_owner AS (
        SELECT b.build_id, l.shard_id, o.owner_fragment_id
        FROM latest_build b
        JOIN session_node_locations l ON l.build_id = b.build_id AND l.node_id = $value
        JOIN session_fragment_owners o ON o.build_id = l.build_id AND o.shard_id = l.shard_id)
      SELECT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM primary_owner p
      JOIN session_shards s ON s.build_id = p.build_id AND s.shard_id = p.shard_id
      UNION
      SELECT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM primary_owner p
      JOIN session_fragment_adjacencies a ON a.build_id = p.build_id AND a.owner_fragment_id = p.owner_fragment_id
      JOIN session_shards s ON s.build_id = a.build_id AND s.shard_id = a.shard_id
      UNION
      SELECT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM latest_build b
      JOIN session_boundary_node_locations l ON l.build_id = b.build_id AND l.node_id = $value
      JOIN session_shards s ON s.build_id = l.build_id AND s.shard_id = l.shard_id
      ORDER BY 1;
      """, nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
  }

  private async Task<IReadOnlyList<CpgShardLocation>> FindSessionBySymbolAsync(CpgSymbolLookup lookup, CancellationToken cancellationToken)
  {
    return await FindSessionLocationsAsync("""
      WITH latest_build AS (
        SELECT b.build_id
        FROM build_sessions b JOIN session_symbol_locations l ON l.build_id = b.build_id
        WHERE b.status = 'Complete' AND l.symbol_key = $value
        ORDER BY b.completed_at_utc DESC, b.build_id DESC LIMIT 1)
      SELECT DISTINCT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM latest_build b
      JOIN session_symbol_locations l ON l.build_id = b.build_id
      JOIN session_shards s ON s.build_id = l.build_id AND s.shard_id = l.shard_id
      WHERE l.symbol_key = $value ORDER BY s.shard_id;
      """, lookup.SymbolKey, cancellationToken);
  }

  private async Task<IReadOnlyList<CpgShardLocation>> FindSessionBySpanAsync(CpgSpanLookup lookup, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      WITH latest_build AS (
        SELECT b.build_id
        FROM build_sessions b
        JOIN session_files f ON f.build_id = b.build_id
        JOIN session_span_locations l ON l.build_id = b.build_id AND l.file_id = f.file_id
        WHERE b.status = 'Complete' AND f.project_id = $projectId
          AND f.relative_path = $relativePath AND f.source_hash = $sourceHash
          AND l.span_start = $spanStart AND l.span_length = $spanLength
        ORDER BY b.completed_at_utc DESC, b.build_id DESC LIMIT 1)
      SELECT DISTINCT s.shard_id, s.shard_path, s.shard_hash, s.byte_length
      FROM latest_build b
      JOIN session_span_locations l ON l.build_id = b.build_id
      JOIN session_shards s ON s.build_id = l.build_id AND s.shard_id = l.shard_id
      WHERE l.span_start = $spanStart AND l.span_length = $spanLength ORDER BY s.shard_id;
      """;
    command.Parameters.AddWithValue("$projectId", lookup.File.ProjectId);
    command.Parameters.AddWithValue("$relativePath", lookup.File.RelativePath);
    command.Parameters.AddWithValue("$sourceHash", lookup.File.SourceHash);
    command.Parameters.AddWithValue("$spanStart", lookup.SpanStart);
    command.Parameters.AddWithValue("$spanLength", lookup.SpanLength);
    return await ReadLocationsAsync(command, cancellationToken);
  }

  private async Task<IReadOnlyList<CpgShardLocation>> FindSessionLocationsAsync(string sql, string value, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$value", value);
    return await ReadLocationsAsync(command, cancellationToken);
  }

  private static async Task EnsureBuildingAsync(SqliteConnection connection, SqliteTransaction transaction, string buildId, CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT status FROM build_sessions WHERE build_id = $buildId;";
    command.Parameters.AddWithValue("$buildId", buildId);
    var status = await command.ExecuteScalarAsync(cancellationToken) as string;
    if (!string.Equals(status, "Building", StringComparison.Ordinal))
    {
      throw new InvalidOperationException("Only an active CPG build session can stage shards.");
    }
  }

  private static async Task<IReadOnlyList<CpgShardLocation>> ReadLocationsAsync(SqliteCommand command, CancellationToken cancellationToken)
  {
    var locations = new List<CpgShardLocation>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      locations.Add(new CpgShardLocation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), CpgShardStatus.Complete));
    }

    return locations;
  }

  private static void BindLookup(SqliteCommand command, CpgShardLookup lookup)
  {
    command.Parameters.AddWithValue("$projectId", lookup.File.ProjectId);
    command.Parameters.AddWithValue("$relativePath", lookup.File.RelativePath);
    command.Parameters.AddWithValue("$sourceHash", lookup.File.SourceHash);
    command.Parameters.AddWithValue("$fragmentKind", lookup.Fragment.Kind);
    command.Parameters.AddWithValue("$spanStart", lookup.Fragment.SpanStart);
    command.Parameters.AddWithValue("$spanLength", lookup.Fragment.SpanLength);
    command.Parameters.AddWithValue("$fragmentHash", lookup.Fragment.FragmentHash);
    command.Parameters.AddWithValue("$schemaVersion", lookup.SchemaVersion);
    command.Parameters.AddWithValue("$profileHash", lookup.ProfileHash);
  }

  private static void BindReusableKey(SqliteCommand command, CpgReusableFragmentKey key)
  {
    command.Parameters.AddWithValue("$projectId", key.ProjectId);
    command.Parameters.AddWithValue("$relativePath", key.RelativePath);
    command.Parameters.AddWithValue("$schemaVersion", key.SchemaVersion);
    command.Parameters.AddWithValue("$profileHash", key.ProfileHash);
    command.Parameters.AddWithValue("$fragmentKind", key.FragmentKind);
    command.Parameters.AddWithValue("$spanStart", key.SpanStart);
    command.Parameters.AddWithValue("$spanLength", key.SpanLength);
    command.Parameters.AddWithValue("$fragmentHash", key.FragmentHash);
    command.Parameters.AddWithValue("$nodeIdFingerprint", key.NodeIdFingerprint);
  }

  private static void ValidateReusableKey(CpgReusableFragmentKey key, CpgShardLookup lookup)
  {
    if (!string.Equals(key.ProjectId, lookup.File.ProjectId, StringComparison.Ordinal) ||
        !string.Equals(key.RelativePath, lookup.File.RelativePath, StringComparison.Ordinal) ||
        key.SchemaVersion != lookup.SchemaVersion ||
        !string.Equals(key.ProfileHash, lookup.ProfileHash, StringComparison.Ordinal) ||
        !string.Equals(key.FragmentKind, lookup.Fragment.Kind, StringComparison.Ordinal) ||
        key.SpanStart != lookup.Fragment.SpanStart ||
        key.SpanLength != lookup.Fragment.SpanLength ||
        !string.Equals(key.FragmentHash, lookup.Fragment.FragmentHash, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(key.NodeIdFingerprint))
    {
      throw new InvalidOperationException("The reusable CPG fragment key does not match its staged shard.");
    }
  }

  private static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params object?[] values)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    for (var index = 0; index < values.Length; index += 1)
    {
      command.Parameters.AddWithValue($"${(char)('a' + index)}", values[index]);
    }

    return await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<int> ExecuteAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string sql,
    CpgCatalogStageTelemetry telemetry,
    CancellationToken cancellationToken,
    params object?[] values)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    for (var index = 0; index < values.Length; index += 1)
    {
      command.Parameters.AddWithValue($"${(char)('a' + index)}", values[index] ?? DBNull.Value);
    }

    var executeStopwatch = Stopwatch.StartNew();
    var executeAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
    executeStopwatch.Stop();
    telemetry.RecordExecuteNonQuery(executeStopwatch.ElapsedMilliseconds, executeAllocatedBytes);
    return affectedRows;
  }

  private static string Hash(params string[] parts)
  {
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)))).ToLowerInvariant();
  }
}

internal sealed class CpgCatalogStageTelemetry
{
  internal long SqlRowCount { get; private set; }
  internal long SqlAffectedRowCount { get; private set; }
  internal long SqlStatementCount { get; private set; }
  internal long TransactionBeginMilliseconds { get; set; }
  internal long EnsureBuildingMilliseconds { get; set; }
  internal long FixedMetadataMilliseconds { get; set; }
  internal long NodeWriteMilliseconds { get; set; }
  internal long SpanWriteMilliseconds { get; set; }
  internal long SymbolWriteMilliseconds { get; set; }
  internal long BoundaryWriteMilliseconds { get; set; }
  internal long TransactionCommitMilliseconds { get; set; }
  internal long RowMaterializationMilliseconds { get; private set; }
  internal long RowMaterializationAllocatedBytes { get; private set; }
  internal long SqlTextBuildMilliseconds { get; private set; }
  internal long SqlTextBuildAllocatedBytes { get; private set; }
  internal long CommandPrepareMilliseconds { get; private set; }
  internal long CommandPrepareAllocatedBytes { get; private set; }
  internal long ExecuteNonQueryMilliseconds { get; private set; }
  internal long ExecuteNonQueryAllocatedBytes { get; private set; }

  internal long ClassifiedMilliseconds =>
    TransactionBeginMilliseconds +
    EnsureBuildingMilliseconds +
    FixedMetadataMilliseconds +
    NodeWriteMilliseconds +
    SpanWriteMilliseconds +
    SymbolWriteMilliseconds +
    BoundaryWriteMilliseconds +
    TransactionCommitMilliseconds;

  internal void RecordStatement(long rows = 0, long affectedRows = 0)
  {
    SqlStatementCount += 1;
    SqlRowCount += rows;
    SqlAffectedRowCount += affectedRows;
  }

  internal void RecordRowMaterialization(long elapsedMilliseconds, long allocatedBytesBefore)
  {
    RowMaterializationMilliseconds += elapsedMilliseconds;
    RowMaterializationAllocatedBytes += Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesBefore);
  }

  internal void RecordSqlTextBuild(long elapsedMilliseconds, long allocatedBytesBefore)
  {
    SqlTextBuildMilliseconds += elapsedMilliseconds;
    SqlTextBuildAllocatedBytes += Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesBefore);
  }

  internal void RecordCommandPrepare(long elapsedMilliseconds, long allocatedBytesBefore)
  {
    CommandPrepareMilliseconds += elapsedMilliseconds;
    CommandPrepareAllocatedBytes += Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesBefore);
  }

  internal void RecordExecuteNonQuery(long elapsedMilliseconds, long allocatedBytesBefore)
  {
    ExecuteNonQueryMilliseconds += elapsedMilliseconds;
    ExecuteNonQueryAllocatedBytes += Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesBefore);
  }
}

internal sealed class SqliteCpgCatalogCommandCache : IAsyncDisposable
{
  private readonly SqliteConnection _connection;
  private readonly Dictionary<string, SqliteCommand> _commands = new(StringComparer.Ordinal);

  internal SqliteCpgCatalogCommandCache(SqliteConnection connection)
  {
    _connection = connection;
  }

  internal async Task<int> ExecuteAsync(
    SqliteTransaction transaction,
    string sql,
    CpgCatalogStageTelemetry telemetry,
    CancellationToken cancellationToken,
    params object?[] values)
  {
    if (!_commands.TryGetValue(sql, out var command))
    {
      var prepareStopwatch = Stopwatch.StartNew();
      var prepareAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
      command = _connection.CreateCommand();
      command.CommandText = sql;
      for (var index = 0; index < values.Length; index += 1)
      {
        command.Parameters.AddWithValue(ParameterName(index), DBNull.Value);
      }

      command.Prepare();
      _commands.Add(sql, command);
      prepareStopwatch.Stop();
      telemetry.RecordCommandPrepare(prepareStopwatch.ElapsedMilliseconds, prepareAllocatedBytes);
    }

    command.Transaction = transaction;
    for (var index = 0; index < values.Length; index += 1)
    {
      command.Parameters[index].Value = values[index] ?? DBNull.Value;
    }

    var executeStopwatch = Stopwatch.StartNew();
    var executeAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
    executeStopwatch.Stop();
    telemetry.RecordExecuteNonQuery(executeStopwatch.ElapsedMilliseconds, executeAllocatedBytes);
    return affectedRows;
  }

  internal async Task<int> ExecuteValuesAsync(
    SqliteTransaction transaction,
    string sql,
    IReadOnlyList<object?> values,
    CpgCatalogStageTelemetry telemetry,
    CancellationToken cancellationToken)
  {
    if (!_commands.TryGetValue(sql, out var command))
    {
      var prepareStopwatch = Stopwatch.StartNew();
      var prepareAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
      command = _connection.CreateCommand();
      command.CommandText = sql;
      for (var index = 0; index < values.Count; index += 1)
      {
        command.Parameters.AddWithValue(ValueParameterName(index), DBNull.Value);
      }

      command.Prepare();
      _commands.Add(sql, command);
      prepareStopwatch.Stop();
      telemetry.RecordCommandPrepare(prepareStopwatch.ElapsedMilliseconds, prepareAllocatedBytes);
    }

    command.Transaction = transaction;
    for (var index = 0; index < values.Count; index += 1)
    {
      command.Parameters[index].Value = values[index] ?? DBNull.Value;
    }

    var executeStopwatch = Stopwatch.StartNew();
    var executeAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
    executeStopwatch.Stop();
    telemetry.RecordExecuteNonQuery(executeStopwatch.ElapsedMilliseconds, executeAllocatedBytes);
    return affectedRows;
  }

  public async ValueTask DisposeAsync()
  {
    foreach (var command in _commands.Values)
    {
      await command.DisposeAsync();
    }

    _commands.Clear();
  }

  private static string ParameterName(int index)
  {
    return "$" + (char)('a' + index);
  }

  private static string ValueParameterName(int index) => $"$p{index}";
}
