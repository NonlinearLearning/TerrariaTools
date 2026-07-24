using Microsoft.Data.Sqlite;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Persistence;
using MinimalRoslynCpg.Persistence.Sqlite;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class SqliteCpgShardCatalogTests
{
  [Fact]
  public void CpgShardStoreLock_CompetingWriter_ThrowsClearException()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      using var firstWriter = CpgShardStoreLock.Acquire(root);

      Assert.Throws<CpgShardStoreLockedException>(() => CpgShardStoreLock.Acquire(root));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task CpgShardStoreLock_CompetingWriter_WaitsUntilFirstWriterReleases()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var firstWriter = CpgShardStoreLock.Acquire(root);
      var waitingWriter = CpgShardStoreLock.AcquireAsync(
        root,
        TimeSpan.FromSeconds(5),
        CancellationToken.None);

      await Task.Delay(100);
      Assert.False(waitingWriter.IsCompleted);
      firstWriter.Dispose();

      using var secondWriter = await waitingWriter.WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task CpgShardStoreLock_CompetingWriter_TimesOutWithoutAcquiring()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      using var firstWriter = CpgShardStoreLock.Acquire(root);

      var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
        CpgShardStoreLock.AcquireAsync(root, TimeSpan.FromMilliseconds(100), CancellationToken.None));

      Assert.Contains(root, exception.Message, StringComparison.Ordinal);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task CpgShardStoreLock_CompetingWriter_HonorsCancellation()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      using var firstWriter = CpgShardStoreLock.Acquire(root);
      using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        CpgShardStoreLock.AcquireAsync(root, TimeSpan.FromSeconds(5), cancellation.Token));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task CpgShardStoreLock_CancellationRacesRelease_DoesNotLeaveTheSemaphoreAcquired()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      for (var attempt = 0; attempt < 20; attempt++)
      {
        var firstWriter = CpgShardStoreLock.Acquire(root);
        using var cancellation = new CancellationTokenSource();
        var waitingWriter = CpgShardStoreLock.AcquireAsync(
          root,
          TimeSpan.FromSeconds(5),
          cancellation.Token);

        cancellation.Cancel();
        firstWriter.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingWriter);

        using var nextWriter = await CpgShardStoreLock.AcquireAsync(
          root,
          TimeSpan.FromSeconds(5),
          CancellationToken.None);
      }
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void CpgShardStoreLock_Dispose_CanBeCalledMoreThanOnce()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var writer = CpgShardStoreLock.Acquire(root);

      writer.Dispose();
      writer.Dispose();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task RebuildFromShardHeadersAsync_DeletedCatalogAndOrphanShard_RebuildsReadableCatalog()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var store = new CpgShardStore(root);
      _ = await store.WriteAsync(CreateShard(lookup), CancellationToken.None);
      File.WriteAllText(Path.Combine(root, "shards", "stale.cpgbin.tmp"), "interrupted");

      var catalogPath = Path.Combine(root, "catalog.db");
      File.WriteAllText(catalogPath, "stale catalog");
      File.Delete(catalogPath);
      store.DeleteStaleTemporaryFiles();
      var catalog = new SqliteCpgShardCatalog(catalogPath);

      var rebuilt = await catalog.RebuildFromShardHeadersAsync(root, CancellationToken.None);
      var acquired = await catalog.TryAcquireAsync(lookup, CancellationToken.None);

      Assert.Equal(1, rebuilt);
      Assert.NotNull(acquired);
      Assert.False(File.Exists(Path.Combine(root, "shards", "stale.cpgbin.tmp")));
      var recovered = await store.ReadAsync(acquired.Location, CancellationToken.None);
      Assert.Equal(lookup, recovered.Lookup);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task RebuildFromShardHeadersAsync_CorruptShard_DoesNotBlockValidShardRecovery()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var store = new CpgShardStore(root);
      _ = await store.WriteAsync(CreateShard(lookup), CancellationToken.None);
      var corruptDirectory = Path.Combine(root, "shards", "broken");
      Directory.CreateDirectory(corruptDirectory);
      await File.WriteAllTextAsync(Path.Combine(corruptDirectory, "broken.cpgbin"), "not a CPG shard");

      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var rebuilt = await catalog.RebuildFromShardHeadersAsync(root, CancellationToken.None);

      Assert.Equal(1, rebuilt);
      Assert.NotNull(await catalog.TryAcquireAsync(lookup, CancellationToken.None));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task PublishAsync_CompleteShard_CanBeAcquiredByMatchingLookup()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var location = new CpgShardLocation("shard", Path.Combine(root, "shard.cpgbin"), "hash", 12, CpgShardStatus.Complete);
      var lease = new CpgShardLease(lookup, location);

      await catalog.PublishAsync(lease, CreateShard(lookup), CancellationToken.None);
      var acquired = await catalog.TryAcquireAsync(lookup, CancellationToken.None);

      Assert.NotNull(acquired);
      Assert.Equal(location, acquired.Location);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task BuildSession_StagedReplacementRemainsInvisibleUntilCompletion()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var oldLocation = new CpgShardLocation("old-shard", Path.Combine(root, "old.cpgbin"), "old-hash", 12, CpgShardStatus.Complete);
      await catalog.PublishAsync(new CpgShardLease(lookup, oldLocation), CreateShard(lookup), CancellationToken.None);
      var begin = typeof(SqliteCpgShardCatalog).GetMethod("BeginBuildAsync");
      var stage = typeof(SqliteCpgShardCatalog).GetMethod("StageAsync");
      var complete = typeof(SqliteCpgShardCatalog).GetMethod("CompleteBuildAsync");

      Assert.NotNull(begin);
      Assert.NotNull(stage);
      Assert.NotNull(complete);
      var buildIdTask = Assert.IsAssignableFrom<Task<string>>(begin!.Invoke(catalog, new object[] { CancellationToken.None }));
      var buildId = await buildIdTask;
      var newLocation = new CpgShardLocation("new-shard", Path.Combine(root, "new.cpgbin"), "new-hash", 12, CpgShardStatus.Complete);
      var stageTask = Assert.IsAssignableFrom<Task>(stage!.Invoke(
        catalog,
        new object[]
        {
          buildId,
          new CpgShardLease(lookup, newLocation),
          CreateShard(lookup),
          CancellationToken.None,
        }));
      await stageTask;

      var beforeCompletion = await catalog.FindByFileAsync(lookup.File, lookup.SchemaVersion, lookup.ProfileHash, CancellationToken.None);
      Assert.Equal(new[] { oldLocation }, beforeCompletion);
      await Assert.IsAssignableFrom<Task>(complete!.Invoke(catalog, new object[] { buildId, CancellationToken.None }));

      var afterCompletion = await catalog.FindByFileAsync(lookup.File, lookup.SchemaVersion, lookup.ProfileHash, CancellationToken.None);
      Assert.Equal(new[] { newLocation }, afterCompletion);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task FindByNodeAsync_CompletedReplacement_ReturnsOnlyNewestBuild()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var firstBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      var firstLocation = new CpgShardLocation("first-shard", Path.Combine(root, "first.cpgbin"), "first-hash", 12, CpgShardStatus.Complete);
      await catalog.StageAsync(firstBuild, new CpgShardLease(lookup, firstLocation), CreateShard(lookup), CancellationToken.None);
      await catalog.CompleteBuildAsync(firstBuild, CancellationToken.None);
      var secondBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      var secondLocation = new CpgShardLocation("second-shard", Path.Combine(root, "second.cpgbin"), "second-hash", 12, CpgShardStatus.Complete);
      await catalog.StageAsync(secondBuild, new CpgShardLease(lookup, secondLocation), CreateShard(lookup), CancellationToken.None);
      await catalog.CompleteBuildAsync(secondBuild, CancellationToken.None);

      var locations = await catalog.FindByNodeAsync(7, CancellationToken.None);

      Assert.Equal(new[] { secondLocation }, locations);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task FindByNodeAsync_BoundaryAdjacency_ReturnsPrimaryAndOwningFragmentAdjacencyOnly()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var primaryLookup = CreateLookup();
      var adjacencyLookup = primaryLookup with
      {
        Fragment = new CpgFragmentKey("boundary-adjacency", 0, 10, "outgoing-adjacency"),
      };
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var buildId = await catalog.BeginBuildAsync(CancellationToken.None);
      var primaryLocation = new CpgShardLocation("primary", Path.Combine(root, "primary.cpgbin"), "primary-hash", 12, CpgShardStatus.Complete);
      var adjacencyLocation = new CpgShardLocation("adjacency", Path.Combine(root, "adjacency.cpgbin"), "adjacency-hash", 12, CpgShardStatus.Complete);
      var adjacency = new CpgFrozenShard(
        adjacencyLookup,
        Array.Empty<CpgFrozenNode>(),
        Array.Empty<CpgFrozenEdge>(),
        Array.Empty<CpgSymbolLocation>(),
        new[] { new CpgFrozenBoundaryEdge(7, 99, "CallTargets", null, null) },
        CpgShardRole.BoundaryAdjacency,
        new CpgBoundaryAdjacency(CreateOwnerFragmentId(primaryLookup), CpgBoundaryAdjacencyDirection.Outgoing));

      await catalog.StageAsync(buildId, new CpgShardLease(primaryLookup, primaryLocation), CreateShard(primaryLookup), CancellationToken.None);
      await catalog.StageAsync(buildId, new CpgShardLease(adjacencyLookup, adjacencyLocation), adjacency, CancellationToken.None);
      await catalog.CompleteBuildAsync(buildId, CancellationToken.None);

      var locations = await catalog.FindByNodeAsync(7, CancellationToken.None);

      Assert.Equal(new[] { adjacencyLocation, primaryLocation }, locations.OrderBy(location => location.ShardId));
      Assert.Equal(new[] { adjacencyLocation }, await catalog.FindByNodeAsync(99, CancellationToken.None));
      await using (var connection = new SqliteConnection(
        $"Data Source={Path.Combine(root, "catalog.db")};Pooling=False"))
      {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM session_node_locations WHERE build_id = $buildId AND node_id = '7';";
        command.Parameters.AddWithValue("$buildId", buildId);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
      }
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task FindBySpanAsync_CompletedReplacement_ReturnsOnlyNewestBuild()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var firstBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      var firstLocation = new CpgShardLocation("first-shard", Path.Combine(root, "first.cpgbin"), "first-hash", 12, CpgShardStatus.Complete);
      await catalog.StageAsync(firstBuild, new CpgShardLease(lookup, firstLocation), CreateShard(lookup), CancellationToken.None);
      await catalog.CompleteBuildAsync(firstBuild, CancellationToken.None);
      var secondBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      var secondLocation = new CpgShardLocation("second-shard", Path.Combine(root, "second.cpgbin"), "second-hash", 12, CpgShardStatus.Complete);
      await catalog.StageAsync(secondBuild, new CpgShardLease(lookup, secondLocation), CreateShard(lookup), CancellationToken.None);
      await catalog.CompleteBuildAsync(secondBuild, CancellationToken.None);

      var locations = await catalog.FindBySpanAsync(new CpgSpanLookup(lookup.File, 0, 10), CancellationToken.None);

      Assert.Equal(new[] { secondLocation }, locations);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task TryAcquireAsync_ProfileDoesNotMatch_ReturnsNull()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var location = new CpgShardLocation("shard", Path.Combine(root, "shard.cpgbin"), "hash", 12, CpgShardStatus.Complete);
      await catalog.PublishAsync(new CpgShardLease(lookup, location), CreateShard(lookup), CancellationToken.None);

      var acquired = await catalog.TryAcquireAsync(lookup with { ProfileHash = "other-profile" }, CancellationToken.None);

      Assert.Null(acquired);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task TryAcquireReusableAsync_ReturnsNewestCompatibleCompletedFragmentOnly()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var firstLookup = CreateLookup() with
      {
        File = new CpgFileKey("project", "input.cs", "first-source"),
        Fragment = new CpgFragmentKey("operation-fragment", 0, 10, "method-source"),
      };
      var firstShard = CreateShard(firstLookup);
      var reusableKey = CpgReusableFragmentKey.Create(firstShard);
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var firstBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      var firstLocation = new CpgShardLocation("first", Path.Combine(root, "first.cpgbin"), "first-hash", 12, CpgShardStatus.Complete);

      await catalog.StageReusableAsync(
        firstBuild,
        new CpgShardLease(firstLookup, firstLocation),
        firstShard,
        reusableKey,
        CancellationToken.None);

      Assert.Null(await catalog.TryAcquireReusableAsync(reusableKey, CancellationToken.None));
      await catalog.CompleteBuildAsync(firstBuild, CancellationToken.None);
      Assert.Equal(firstLocation, (await catalog.TryAcquireReusableAsync(reusableKey, CancellationToken.None))!.Location);

      var secondBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      var secondLookup = firstLookup with
      {
        File = firstLookup.File with { SourceHash = "second-source" },
      };
      var secondLocation = new CpgShardLocation("second", Path.Combine(root, "second.cpgbin"), "second-hash", 12, CpgShardStatus.Complete);
      await catalog.StageReusableAsync(
        secondBuild,
        new CpgShardLease(secondLookup, secondLocation),
        CreateShard(secondLookup),
        reusableKey,
        CancellationToken.None);
      await catalog.CompleteBuildAsync(secondBuild, CancellationToken.None);

      Assert.Equal(secondLocation, (await catalog.TryAcquireReusableAsync(reusableKey, CancellationToken.None))!.Location);
      Assert.Null(await catalog.TryAcquireReusableAsync(
        reusableKey with { NodeIdFingerprint = "incompatible" },
        CancellationToken.None));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task StageAsync_MultipleFragmentsForOneFile_InsertsSessionFileOnce()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var firstLookup = CreateLookup();
      var secondLookup = firstLookup with
      {
        Fragment = new CpgFragmentKey("method", 11, 10, "second-fragment"),
      };
      var catalogPath = Path.Combine(root, "catalog.db");
      var catalog = new SqliteCpgShardCatalog(catalogPath);
      var buildId = await catalog.BeginBuildAsync(CancellationToken.None);
      await using (var connection = new SqliteConnection($"Data Source={catalogPath};Pooling=False"))
      {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
          CREATE TABLE session_file_audit (count INTEGER NOT NULL);
          INSERT INTO session_file_audit(count) VALUES (0);
          CREATE TRIGGER session_file_insert_audit AFTER INSERT ON session_files
          BEGIN
            UPDATE session_file_audit SET count = count + 1;
          END;
          """;
        await command.ExecuteNonQueryAsync();
      }

      await catalog.StageAsync(
        buildId,
        new CpgShardLease(firstLookup, CreateLocation(root, "first")),
        CreateShard(firstLookup),
        CancellationToken.None);
      await catalog.StageAsync(
        buildId,
        new CpgShardLease(secondLookup, CreateLocation(root, "second")),
        CreateShard(secondLookup),
        CancellationToken.None);

      await using var verifyConnection = new SqliteConnection($"Data Source={catalogPath};Pooling=False");
      await verifyConnection.OpenAsync();
      await using var verify = verifyConnection.CreateCommand();
      verify.CommandText = "SELECT count FROM session_file_audit;";
      Assert.Equal(1L, (long)(await verify.ExecuteScalarAsync())!);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task StageAsync_LargeLocationSets_ChunksValuesWithinSqliteParameterLimit()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var buildId = await catalog.BeginBuildAsync(CancellationToken.None);
      var shard = CreateLargeShard(lookup, 1024);

      await catalog.StageAsync(
        buildId,
        new CpgShardLease(lookup, CreateLocation(root, "large")),
        shard,
        CancellationToken.None);
      await catalog.CompleteBuildAsync(buildId, CancellationToken.None);

      Assert.Single(await catalog.FindByNodeAsync(1023, CancellationToken.None));
      Assert.Single(await catalog.FindBySpanAsync(
        new CpgSpanLookup(lookup.File, 10230, 10),
        CancellationToken.None));
      Assert.Single(await catalog.FindBySymbolAsync(
        new CpgSymbolLookup("symbol-1023"),
        CancellationToken.None));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task CompleteBuildAsync_SharedShard_IncrementsReferenceWithoutRebuildingHistory()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var catalogPath = Path.Combine(root, "catalog.db");
      var catalog = new SqliteCpgShardCatalog(catalogPath);
      var location = CreateLocation(root, "shared");
      var firstBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      await catalog.StageAsync(
        firstBuild,
        new CpgShardLease(lookup, location),
        CreateShard(lookup),
        CancellationToken.None);
      await catalog.CompleteBuildAsync(firstBuild, CancellationToken.None);

      await using (var connection = new SqliteConnection($"Data Source={catalogPath};Pooling=False"))
      {
        await connection.OpenAsync();
        await using var trigger = connection.CreateCommand();
        trigger.CommandText = """
          CREATE TRIGGER reject_physical_reference_rebuild
          BEFORE DELETE ON physical_shard_references
          BEGIN
            SELECT RAISE(ABORT, 'physical reference rebuild is not allowed');
          END;
          """;
        await trigger.ExecuteNonQueryAsync();
      }

      var secondBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      await catalog.StageAsync(
        secondBuild,
        new CpgShardLease(lookup, location),
        CreateShard(lookup),
        CancellationToken.None);

      await catalog.CompleteBuildAsync(secondBuild, CancellationToken.None);

      await using var verifyConnection = new SqliteConnection($"Data Source={catalogPath};Pooling=False");
      await verifyConnection.OpenAsync();
      await using var reference = verifyConnection.CreateCommand();
      reference.CommandText = "SELECT reference_count FROM physical_shard_references WHERE shard_path = $path;";
      reference.Parameters.AddWithValue("$path", location.ShardPath);
      Assert.Equal(2L, (long)(await reference.ExecuteScalarAsync())!);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task PruneCompletedBuildsAsync_SharedPhysicalShard_KeepsReachableFileThenDeletesIt()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var lookup = CreateLookup();
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var shardPath = Path.Combine(root, "shared.cpgbin");
      await File.WriteAllTextAsync(shardPath, "shard");
      var location = new CpgShardLocation("shared", shardPath, "shared-hash", 5, CpgShardStatus.Complete);
      var firstBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      await catalog.StageAsync(firstBuild, new CpgShardLease(lookup, location), CreateShard(lookup), CancellationToken.None);
      await catalog.CompleteBuildAsync(firstBuild, CancellationToken.None);
      await Task.Delay(20);
      var secondBuild = await catalog.BeginBuildAsync(CancellationToken.None);
      await catalog.StageAsync(secondBuild, new CpgShardLease(lookup, location), CreateShard(lookup), CancellationToken.None);
      await catalog.CompleteBuildAsync(secondBuild, CancellationToken.None);

      var firstPrune = await catalog.PruneCompletedBuildsAsync(1, CancellationToken.None);

      Assert.Equal(1, firstPrune.PrunedBuildCount);
      Assert.Equal(0, firstPrune.DeletedShardCount);
      Assert.True(File.Exists(shardPath));
      Assert.Single(await catalog.FindByFileAsync(lookup.File, lookup.SchemaVersion, lookup.ProfileHash, CancellationToken.None));

      var finalPrune = await catalog.PruneCompletedBuildsAsync(0, CancellationToken.None);

      Assert.Equal(1, finalPrune.PrunedBuildCount);
      Assert.Equal(1, finalPrune.DeletedShardCount);
      Assert.False(File.Exists(shardPath));
      Assert.Empty(await catalog.FindByFileAsync(lookup.File, lookup.SchemaVersion, lookup.ProfileHash, CancellationToken.None));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static CpgShardLookup CreateLookup()
  {
    return new CpgShardLookup(
      new CpgFileKey("project", "input.cs", "source"),
      new CpgFragmentKey("method", 0, 10, "fragment"),
      1,
      "profile");
  }

  private static CpgFrozenShard CreateShard(CpgShardLookup lookup)
  {
    return new CpgFrozenShard(
      lookup,
      new[] { new CpgFrozenNode(0, 7, "Operation", "input.cs", 0, 10, "Operation", null, null, null, false) },
      Array.Empty<CpgFrozenEdge>(),
      new[] { new CpgSymbolLocation("symbol", 0) });
  }

  private static CpgFrozenShard CreateLargeShard(CpgShardLookup lookup, int count)
  {
    var nodes = Enumerable.Range(0, count)
      .Select(index => new CpgFrozenNode(
        index,
        (uint)index,
        "Operation",
        "input.cs",
        index * 10,
        (index * 10) + 10,
        "Operation",
        null,
        null,
        null,
        false))
      .ToArray();
    var symbols = Enumerable.Range(0, count)
      .Select(index => new CpgSymbolLocation($"symbol-{index}", index))
      .ToArray();
    return new CpgFrozenShard(lookup, nodes, Array.Empty<CpgFrozenEdge>(), symbols);
  }

  private static CpgShardLocation CreateLocation(string root, string shardId)
  {
    return new CpgShardLocation(
      shardId,
      Path.Combine(root, $"{shardId}.cpgbin"),
      $"{shardId}-hash",
      12,
      CpgShardStatus.Complete);
  }

  private static string CreateOwnerFragmentId(CpgShardLookup lookup)
  {
    return string.Join("|", lookup.File.ProjectId, lookup.File.RelativePath, lookup.File.SourceHash,
      lookup.Fragment.Kind, lookup.Fragment.SpanStart, lookup.Fragment.SpanLength,
      lookup.Fragment.FragmentHash, lookup.SchemaVersion, lookup.ProfileHash);
  }

  private static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(Path.GetTempPath(), "sqlite-cpg-shard-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
  }
}
