using Microsoft.Data.Sqlite;
using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using MinimalRoslynCpg.Persistence;
using MinimalRoslynCpg.Persistence.Sqlite;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace RoslynPrototype.Tests;

[Collection("CpgPersistenceObserver")]
public sealed class CpgShardBuildCoordinatorTests
{
  [Fact]
  public async Task BuildFromSource_Persistence_DeletedCatalog_RebuildsCompletedSession()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-completed-session-rebuild-tests", Guid.NewGuid().ToString("N"));
    try
    {
      const string source = "class Example { int First(int value) => Second(value) + 1; int Second(int value) => value + 2; }";
      var persistence = new CpgPersistenceOptions(
        root,
        "completed-session-rebuild",
        StreamingMode: true);
      _ = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = persistence,
      }).BuildFromSource(source, "input.cs");

      File.Delete(Path.Combine(root, "catalog.db"));
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));

      var rebuilt = await catalog.RebuildFromShardHeadersAsync(root, CancellationToken.None);

      Assert.True(rebuilt > 0);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Theory]
  [InlineData(1)]
  [InlineData(8)]
  [InlineData(12)]
  [InlineData(14)]
  [InlineData(16)]
  public async Task BuildFromSource_Persistence_ShardBackedSliceMatchesSerialAtConfiguredDop(
    int maxDegreeOfParallelism)
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-persistence-slice-dop-tests", Guid.NewGuid().ToString("N"));
    try
    {
      const string source = "class Example { int First(int value) => Second(value) + 1; int Second(int value) => value + 2; }";
      var serial = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        MaxDegreeOfParallelism = 1,
      }).BuildFromSource(source, "input.cs");
      var persisted = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        MaxDegreeOfParallelism = maxDegreeOfParallelism,
        Persistence = new CpgPersistenceOptions(
          root,
          $"slice-dop-{maxDegreeOfParallelism}",
          StreamingMode: true),
      }).BuildFromSource(source, "input.cs");
      var sink = serial.Edges.First(edge => edge.Kind == RoslynCpgEdgeKind.DataFlow).TargetNodeId;
      var options = new RoslynCpgSliceQueryOptions(
        new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
        MaxHops: 4,
        MaxPaths: 8,
        MaxDefinitions: 8);
      var expected = new RoslynCpgSliceQuery(serial).QueryBackward(sink, options);
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var actual = await new RoslynCpgSliceQuery(new CpgShardQueryResolver(
        catalog,
        new CpgShardStore(root),
        maxCachedBytes: 1024 * 1024)).QueryBackwardAsync(sink, options, CancellationToken.None);

      Assert.Equal(serial.GraphSnapshotVersion, persisted.GraphSnapshotVersion);
      Assert.Equal(expected.Paths.Select(path => path.NodeIds), actual.Paths.Select(path => path.NodeIds));
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_Persistence_WorkerFailureInvalidatesSessionAndCleansStaging()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-worker-failure-tests", Guid.NewGuid().ToString("N"));
    var sessionType = typeof(RoslynCpgBuilder).Assembly.GetType("MinimalRoslynCpg.Builder.CpgShardBuildSession");
    var observer = sessionType?.GetProperty(
      "CheckpointObserver",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(observer);
    const string source = "class Example { int First() => Second(); int Second() => 2; }";
    try
    {
      observer!.SetValue(null, (Action<object>)(_ => throw new InvalidOperationException("injected worker failure")));
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(
          root,
          "worker-failure-profile",
          StreamingMode: true),
      });

      var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
        builder.BuildFromSource(source, "input.cs")));

      Assert.Equal("injected worker failure", exception.Message);
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var file = new CpgFileKey(
        Path.GetFullPath("."),
        "input.cs",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
      Assert.Empty(await catalog.FindByFileAsync(file, 1, "worker-failure-profile", CancellationToken.None));
      Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "builds")));
    }
    finally
    {
      observer!.SetValue(null, null);
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_Persistence_ConcurrentSharedStoreWaitsAndBothBuildsComplete()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-shared-store-wait-tests", Guid.NewGuid().ToString("N"));
    var firstCheckpointReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirstBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var sessionType = typeof(RoslynCpgBuilder).Assembly.GetType("MinimalRoslynCpg.Builder.CpgShardBuildSession");
    var observer = sessionType?.GetProperty(
      "CheckpointObserver",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(observer);
    try
    {
      observer!.SetValue(null, (Action<object>)(_ =>
      {
        if (firstCheckpointReached.TrySetResult())
        {
          releaseFirstBuild.Task.GetAwaiter().GetResult();
        }
      }));
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "shared-store-profile", StoreLockWaitMilliseconds: 5000),
      };
      var first = new RoslynCpgBuilder(options);
      var second = new RoslynCpgBuilder(options);
      var firstTask = Task.Run(() => first.BuildFromSource("class First { int Run() => 1; }", "first.cs"));

      await firstCheckpointReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
      var secondTask = Task.Run(() => second.BuildFromSource("class Second { int Run() => 2; }", "second.cs"));
      await Task.Delay(100);
      Assert.False(secondTask.IsCompleted);

      releaseFirstBuild.TrySetResult();
      var firstGraph = await firstTask.WaitAsync(TimeSpan.FromSeconds(10));
      var secondGraph = await secondTask.WaitAsync(TimeSpan.FromSeconds(10));

      Assert.True(firstGraph.HasQueryIndex);
      Assert.True(secondGraph.HasQueryIndex);
      Assert.NotEmpty(Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories));
    }
    finally
    {
      releaseFirstBuild.TrySetResult();
      observer!.SetValue(null, null);
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_Persistence_CompletedSessionRestoreDoesNotWaitForAnotherWriter()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-restore-during-write-tests", Guid.NewGuid().ToString("N"));
    var writerCheckpointReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Task? writerTask = null;
    Task<MinimalRoslynCpg.Model.RoslynCpgGraph>? restoreTask = null;
    var sessionType = typeof(RoslynCpgBuilder).Assembly.GetType("MinimalRoslynCpg.Builder.CpgShardBuildSession");
    var observer = sessionType?.GetProperty(
      "CheckpointObserver",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(observer);
    const string restoredSource = "class Restored { int Run() => 1; }";
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "restore-during-write-profile", StreamingMode: true),
      };
      var expected = new RoslynCpgBuilder(options).BuildFromSource(restoredSource, "restored.cs");

      observer!.SetValue(null, (Action<object>)(_ =>
      {
        if (writerCheckpointReached.TrySetResult())
        {
          releaseWriter.Task.GetAwaiter().GetResult();
        }
      }));
      var writer = new RoslynCpgBuilder(options);
      writerTask = Task.Run(() => writer.BuildFromSource("class Writer { int Run() => 2; }", "writer.cs"));
      await writerCheckpointReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

      var restoringBuilder = new RoslynCpgBuilder(options);
      restoreTask = Task.Run(() => restoringBuilder.BuildFromSource(restoredSource, "restored.cs"));
      var restored = await restoreTask.WaitAsync(TimeSpan.FromSeconds(2));

      Assert.Equal(expected.GraphSnapshotVersion, restored.GraphSnapshotVersion);
      Assert.Empty(restoringBuilder.LastBuildTelemetry.ExecutedPassNames!);
      Assert.False(writerTask.IsCompleted);
    }
    finally
    {
      releaseWriter.TrySetResult();
      observer!.SetValue(null, null);
      if (writerTask is not null)
      {
        await writerTask.WaitAsync(TimeSpan.FromSeconds(10));
      }
      if (restoreTask is not null)
      {
        await restoreTask.WaitAsync(TimeSpan.FromSeconds(10));
      }
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_Persistence_UsesConfiguredConcurrentFileWriteLimit()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-concurrent-file-write-tests", Guid.NewGuid().ToString("N"));
    var twoWritesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var allowWritesToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var sessionType = typeof(RoslynCpgBuilder).Assembly.GetType("MinimalRoslynCpg.Builder.CpgShardBuildSession");
    var observer = sessionType?.GetProperty(
      "CheckpointObserver",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(observer);
    try
    {
      observer!.SetValue(null, (Action<object>)(checkpoint =>
      {
        var activeWrites = (int)checkpoint.GetType().GetProperty("ActiveFileWriteCount")!.GetValue(checkpoint)!;
        if (activeWrites >= 2)
        {
          twoWritesStarted.TrySetResult();
        }

        allowWritesToComplete.Task.GetAwaiter().GetResult();
      }));
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(
          root,
          "concurrent-file-write-profile",
          MaxConcurrentShardFileWrites: 2),
      });
      var buildTask = Task.Run(() => builder.BuildFromSource("""
        class Example
        {
          int First() => 1;
          int Second() => 2;
          int Third() => First() + Second();
        }
        """, "input.cs"));

      await twoWritesStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
      allowWritesToComplete.TrySetResult();
      _ = await buildTask.WaitAsync(TimeSpan.FromSeconds(10));

      var persistence = Assert.IsType<CpgPersistenceTelemetry>(builder.LastBuildTelemetry.Persistence);
      Assert.Equal(2, persistence.PeakConcurrentFileWrites);
    }
    finally
    {
      allowWritesToComplete.TrySetResult();
      observer!.SetValue(null, null);
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_Persistence_UsesConfiguredConcurrentShardExportLimit()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-concurrent-shard-export-tests", Guid.NewGuid().ToString("N"));
    var twoExportsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var allowExportsToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var coordinatorType = typeof(RoslynCpgBuilder).Assembly.GetType("MinimalRoslynCpg.Builder.CpgShardBuildCoordinator");
    var observer = coordinatorType?.GetProperty(
      "ExportCheckpointObserver",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(observer);
    try
    {
      observer!.SetValue(null, (Action<object>)(checkpoint =>
      {
        var activeExports = (int)checkpoint.GetType().GetProperty("ActiveExportCount")!.GetValue(checkpoint)!;
        if (activeExports >= 2)
        {
          twoExportsStarted.TrySetResult();
        }

        allowExportsToComplete.Task.GetAwaiter().GetResult();
      }));
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(
          root,
          "concurrent-shard-export-profile",
          MaxConcurrentShardExports: 2,
          MaxConcurrentShardFileWrites: 1),
      });
      var buildTask = Task.Run(() => builder.BuildFromSource("""
        class Example
        {
          int First() => 1;
          int Second() => 2;
          int Third() => First() + Second();
        }
        """, "input.cs"));

      await twoExportsStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
      allowExportsToComplete.TrySetResult();
      _ = await buildTask.WaitAsync(TimeSpan.FromSeconds(10));

      var persistence = Assert.IsType<CpgPersistenceTelemetry>(builder.LastBuildTelemetry.Persistence);
      Assert.Equal(2, persistence.PeakConcurrentShardExports);
    }
    finally
    {
      allowExportsToComplete.TrySetResult();
      observer!.SetValue(null, null);
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_StreamingPersistence_StagedPrepublishRemainsInvisibleUntilCompletion()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-invisible-prepublish-tests", Guid.NewGuid().ToString("N"));
    var checkpointReached = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
    var allowCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var sessionType = typeof(RoslynCpgBuilder).Assembly.GetType("MinimalRoslynCpg.Builder.CpgShardBuildSession");
    var observer = sessionType?.GetProperty(
      "CheckpointObserver",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(observer);
    try
    {
      observer!.SetValue(null, (Action<object>)(checkpoint =>
      {
        var kind = (string)checkpoint.GetType().GetProperty("FragmentKind")!.GetValue(checkpoint)!;
        if (kind != "LocalFunctionStatementSyntax")
        {
          return;
        }

        checkpointReached.TrySetResult(checkpoint);
        allowCompletion.Task.GetAwaiter().GetResult();
      }));
      const string source = "class Example { int First() => 1; void Second() { void Nested() { var value = 2; } Nested(); } }";
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "invisible-prepublish-profile", StreamingMode: true),
      });
      var buildTask = Task.Run(() => builder.BuildFromSource(source, "input.cs"));

      var checkpoint = await checkpointReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
      var stagingRoot = (string)checkpoint.GetType().GetProperty("StagingRoot")!.GetValue(checkpoint)!;
      var stagedStore = new CpgShardStore(stagingRoot);
      var stagedShards = new List<CpgFrozenShard>();
      foreach (var shardPath in Directory.EnumerateFiles(stagingRoot, "*.cpgbin", SearchOption.AllDirectories))
      {
        var (shard, _) = await stagedStore.ReadFromPathAsync(shardPath, CancellationToken.None);
        stagedShards.Add(shard);
      }

      Assert.Contains(stagedShards, shard => shard.Lookup.Fragment.Kind == "file-skeleton");
      Assert.DoesNotContain(stagedShards.SelectMany(shard => shard.Nodes), node => node.Kind == "Operation");
      var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
      var file = new CpgFileKey(
        Path.GetFullPath("."),
        "input.cs",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
      var visible = await catalog.FindByFileAsync(file, 1, "invisible-prepublish-profile", CancellationToken.None);
      Assert.Empty(visible);

      allowCompletion.SetResult();
      _ = await buildTask.WaitAsync(TimeSpan.FromSeconds(10));
      Assert.False(File.Exists(Path.Combine(root, ".cpg-store.lock")));
    }
    finally
    {
      allowCompletion.TrySetResult();
      observer!.SetValue(null, null);
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_StreamingPersistence_PrepublishesSkeletonAndMethodBoundariesBeforeOperationFragments()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-prepublish-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "prepublish-profile", StreamingMode: true),
      });

      _ = builder.BuildFromSource("""
        class Example
        {
          int First() => 1;
          void Second() { void Nested() { var value = 2; } Nested(); }
        }
        """, "input.cs");

      var telemetry = Assert.IsType<RoslynCpgStreamingFragmentTelemetry>(builder.LastBuildTelemetry.StreamingFragments);
      Assert.Equal("file-skeleton", telemetry.PublishedKinds[0]);
      Assert.Equal(
        new[] { "MethodDeclarationSyntax", "MethodDeclarationSyntax", "LocalFunctionStatementSyntax" },
        telemetry.PublishedKinds.Skip(1).Take(3));
      Assert.Equal(new[] { "operation-fragment", "operation-fragment" },
        telemetry.PublishedKinds.Skip(4).Take(2));

      var store = new CpgShardStore(root);
      var shards = new List<CpgFrozenShard>();
      foreach (var path in Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories))
      {
        var (shard, _) = await store.ReadFromPathAsync(path, CancellationToken.None);
        shards.Add(shard);
      }

      var skeleton = Assert.Single(shards, shard => shard.Lookup.Fragment.Kind == "file-skeleton");
      Assert.DoesNotContain(skeleton.Nodes, node => node.Kind == "Operation");
      var boundaryShards = shards
        .Where(shard => shard.Lookup.Fragment.Kind is "MethodDeclarationSyntax" or "LocalFunctionStatementSyntax")
        .ToArray();
      Assert.DoesNotContain(boundaryShards.SelectMany(shard => shard.Nodes), node => node.Kind == "Operation");
      var methodBoundaryNodeIds = boundaryShards
        .SelectMany(shard => shard.Nodes)
        .Where(node => node.Kind is "Method" or "MethodParameter" or "MethodReturn" or "MethodEntry" or "MethodExit")
        .Select(node => node.NodeId)
        .ToArray();
      Assert.Equal(methodBoundaryNodeIds.Length, methodBoundaryNodeIds.Distinct().Count());

      var operationNodeIds = shards
        .Where(shard => shard.Lookup.Fragment.Kind == "operation-fragment")
        .SelectMany(shard => shard.Nodes)
        .Where(node => node.Kind == "Operation")
        .Select(node => node.NodeId)
        .ToArray();
      Assert.NotEmpty(operationNodeIds);
      Assert.Equal(operationNodeIds.Length, operationNodeIds.Distinct().Count());

    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void BuildFromSource_StreamingPersistence_PublishesMethodShardsInSourceOrderWithoutFileGraph()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-shard-build-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(
          root,
          "streaming-test-profile",
          StreamingMode: true),
      };

      var builder = new RoslynCpgBuilder(options);

      _ = builder.BuildFromSource("""
        class Example
        {
          int First() => Second();
          int Second() => 2;
          int Third() => 3;
        }
        """, "input.cs");

      var telemetry = Assert.IsType<RoslynCpgStreamingFragmentTelemetry>(
        builder.LastBuildTelemetry.StreamingFragments);
      Assert.Equal(new[] { 0, 1, 2 }, telemetry.PublishedOrders);
      Assert.Equal(3, telemetry.ReleasedFragmentCount);
      Assert.InRange(telemetry.PeakRetainedFragmentCount, 0, 1);
      Assert.DoesNotContain("file-graph", telemetry.PublishedKinds);
      Assert.Contains("boundary-adjacency", telemetry.PublishedKinds);
      Assert.True(builder.LastBuildTelemetry.Preallocation?.UsedAnchorDiscovery);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_StreamingPersistence_LocalFunctionDescriptorsHaveUniqueShardOwnership()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-local-function-ownership-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "local-function-ownership-profile", StreamingMode: true),
      });

      _ = builder.BuildFromSource("""
        class Example
        {
          void Outer()
          {
            void Inner() { var value = 1; }
            Inner();
          }
        }
        """, "input.cs");

      var store = new CpgShardStore(root);
      var nodeIds = new List<uint>();
      foreach (var shardPath in Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories))
      {
        var (shard, _) = await store.ReadFromPathAsync(shardPath, CancellationToken.None);
        nodeIds.AddRange(shard.Nodes.Select(node => node.NodeId));
      }

      Assert.Empty(nodeIds.GroupBy(nodeId => nodeId).Where(group => group.Count() > 1));
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_StreamingPersistence_DeduplicatesOperationNodesWithSharedStableAnchor()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-operation-anchor-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "operation-anchor-profile", StreamingMode: true),
      });

      _ = builder.BuildFromSource("""
        class Example
        {
          long Convert(object boxed)
          {
            long value = (int)boxed;
            return value;
          }
        }
        """, "input.cs");

      var store = new CpgShardStore(root);
      var operationNodeIds = new List<uint>();
      foreach (var shardPath in Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories))
      {
        var (shard, _) = await store.ReadFromPathAsync(shardPath, CancellationToken.None);
        operationNodeIds.AddRange(shard.Nodes
          .Where(node => node.Kind == "Operation")
          .Select(node => node.NodeId));
      }

      Assert.NotEmpty(operationNodeIds);
      Assert.Equal(operationNodeIds.Count, operationNodeIds.Distinct().Count());
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_StreamingPersistence_PublishesFragmentOwnedBoundaryAdjacencyWithoutGlobalManifest()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-boundary-manifest-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "boundary-manifest-profile", StreamingMode: true),
      });

      _ = builder.BuildFromSource("class Example { int First() => Second(); int Second() => 2; }", "input.cs");

      var store = new CpgShardStore(root);
      var shards = new List<CpgFrozenShard>();
      foreach (var path in Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories))
      {
        var (shard, _) = await store.ReadFromPathAsync(path, CancellationToken.None);
        shards.Add(shard);
      }

      var adjacencyShards = shards
        .Where(shard => shard.Lookup.Fragment.Kind == "boundary-adjacency")
        .ToArray();
      Assert.DoesNotContain(shards, shard => shard.Lookup.Fragment.Kind == "cross-shard-edges");
      Assert.NotEmpty(adjacencyShards);
      Assert.All(adjacencyShards, shard =>
      {
        Assert.Empty(shard.Nodes);
        Assert.NotEmpty(shard.BoundaryEdges!);
        Assert.NotNull(typeof(CpgFrozenShard).GetProperty("BoundaryAdjacency"));
      });
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void BuildFromSource_StreamingPersistence_RestoresGraphFromPublishedShards()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-shard-restore-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "streaming-restore-profile", StreamingMode: true),
      };
      const string source = "class Example { int First() => Second(); int Second() => 2; }";

      var original = new RoslynCpgBuilder(options).BuildFromSource(source, "input.cs");
      var restoredBuilder = new RoslynCpgBuilder(options);
      var restored = restoredBuilder.BuildFromSource(source, "input.cs");

      Assert.Equal(original.GraphSnapshotVersion, restored.GraphSnapshotVersion);
      Assert.Empty(restoredBuilder.LastBuildTelemetry.ExecutedPassNames!);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void BuildFromSource_StreamingPersistence_ReportsBoundedCatalogBatchTelemetry()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-catalog-batch-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(
          root,
          "catalog-batch-profile",
          StreamingMode: true,
          MaxCatalogBatchRows: 2,
          MaxPendingShardPublications: 2),
      };

      var builder = new RoslynCpgBuilder(options);
      _ = builder.BuildFromSource(
        "class Example { int First() => Second(); int Second() => 2; int Third() => First(); }",
        "input.cs");

      var persistence = Assert.IsType<CpgPersistenceTelemetry>(builder.LastBuildTelemetry.Persistence);
      Assert.True(persistence.CatalogBatchCount > 0);
      Assert.InRange(persistence.PeakQueueDepth, 0, 2);
      Assert.Equal(persistence.CatalogBatchCount, persistence.CatalogBatchRows!.Count);
      Assert.All(persistence.CatalogBatchRows, rows => Assert.True(rows > 0));
      Assert.True(persistence.StoreLockWaitMilliseconds >= 0);
      Assert.True(persistence.SerializationMilliseconds >= 0);
      Assert.True(persistence.ValidationMilliseconds >= 0);
      Assert.True(persistence.FlushMilliseconds >= 0);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public async Task BuildFromSource_StreamingPersistence_CatalogStagesOperationFragmentsInSourceOrderWhenWritesCompleteOutOfOrder()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-catalog-order-tests", Guid.NewGuid().ToString("N"));
    var completedOperationSpans = new List<int>();
    var completedOperationGate = new object();
    var sessionType = typeof(RoslynCpgBuilder).Assembly.GetType("MinimalRoslynCpg.Builder.CpgShardBuildSession");
    var observer = sessionType?.GetProperty(
      "CheckpointObserver",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(observer);
    try
    {
      observer!.SetValue(null, (Action<object>)(checkpoint =>
      {
        var kind = (string)checkpoint.GetType().GetProperty("FragmentKind")!.GetValue(checkpoint)!;
        if (!string.Equals(kind, "operation-fragment", StringComparison.Ordinal))
        {
          return;
        }

        var spanStart = (int)checkpoint.GetType().GetProperty("FragmentSpanStart")!.GetValue(checkpoint)!;
        lock (completedOperationGate)
        {
          completedOperationSpans.Add(spanStart);
        }
      }));
      var builder = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        MaxDegreeOfParallelism = 2,
        Persistence = new CpgPersistenceOptions(
          root,
          "catalog-source-order-profile",
          StreamingMode: true,
          MaxConcurrentShardExports: 2,
          MaxConcurrentShardFileWrites: 2),
      });

      var source = CreateOutOfOrderOperationFragmentSource();
      var expected = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault()).BuildFromSource(source, "input.cs");
      var persisted = await Task.Run(() => builder.BuildFromSource(source, "input.cs"));

      var completionOrder = completedOperationSpans.ToArray();
      Assert.Equal(2, completionOrder.Length);
      Assert.NotEqual(completionOrder.OrderBy(span => span).ToArray(), completionOrder);

      var stagedOrder = await ReadCompletedOperationFragmentStageOrderAsync(Path.Combine(root, "catalog.db"));
      Assert.Equal(completionOrder.OrderBy(span => span).ToArray(), stagedOrder);
      Assert.Equal(expected.GraphSnapshotVersion, persisted.GraphSnapshotVersion);
    }
    finally
    {
      observer!.SetValue(null, null);
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void BuildFromSource_StreamingPersistence_ReusesUnchangedOperationFragmentAfterLaterMethodEdit()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-reuse-tests", Guid.NewGuid().ToString("N"));
    var readObserver = typeof(CpgShardStore).GetProperty(
      "AfterReadForTesting",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(readObserver);
    var readCount = 0;
    readObserver!.SetValue(null, (Action<CpgShardLocation>)(_ => Interlocked.Increment(ref readCount)));
    try
    {
      const string firstSource = "class Example { int First() => 1; int Second() => 2; }";
      const string secondSource = "class Example { int First() => 1; int Second() => 3; }";
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "reuse-profile", StreamingMode: true),
      };
      var builder = new RoslynCpgBuilder(options);
      _ = builder.BuildFromSource(firstSource, "input.cs");
      var reusedBuild = builder.BuildFromSource(secondSource, "input.cs");
      var expected = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault())
        .BuildFromSource(secondSource, "input.cs");

      var persistence = Assert.IsType<CpgPersistenceTelemetry>(builder.LastBuildTelemetry.Persistence);
      Assert.True(persistence.ReusedShardCount >= 1);
      Assert.True(persistence.ReuseMissCount >= 1);
      Assert.True(persistence.ReusedShardBytes > 0);
      Assert.Equal(0, readCount);
      Assert.Equal(expected.GraphSnapshotVersion, reusedBuild.GraphSnapshotVersion);
    }
    finally
    {
      readObserver!.SetValue(null, null);
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task BuildFromSource_StreamingPersistence_RepeatedChangedMethodReuseSamples_CompleteAndLeaveStoreReusable()
  {
    for (var iteration = 0; iteration < 4; iteration += 1)
    {
      var root = Path.Combine(
        Path.GetTempPath(),
        "cpg-streaming-reuse-sample-tests",
        Guid.NewGuid().ToString("N"));
      try
      {
        var options = RoslynCpgBuilderOptions.CreateDefault() with
        {
          MaxDegreeOfParallelism = 12,
          Persistence = new CpgPersistenceOptions(root, "repeated-reuse-profile", StreamingMode: true),
        };
        var buildTask = Task.Run(() =>
        {
          var builder = new RoslynCpgBuilder(options);
          _ = builder.BuildFromSource(CreateReuseFixtureSource(changedLiteral: 95), "input.cs");
          var reusedBuild = builder.BuildFromSource(CreateReuseFixtureSource(changedLiteral: 94), "input.cs");
          var persistence = Assert.IsType<CpgPersistenceTelemetry>(builder.LastBuildTelemetry.Persistence);
          Assert.True(persistence.ReusedShardCount >= 1);
          Assert.True(persistence.ReuseMissCount >= 1);
          Assert.True(persistence.ReusedShardBytes > 0);
          Assert.True(reusedBuild.HasQueryIndex);
        });

        await buildTask.WaitAsync(TimeSpan.FromSeconds(30));
        using var reopened = await CpgPersistenceTestKit.AcquireStoreLockAsync(
          root,
          TimeSpan.FromSeconds(5),
          CancellationToken.None);
      }
      finally
      {
        if (Directory.Exists(root))
        {
          Directory.Delete(root, recursive: true);
        }
      }
    }
  }

  [Theory]
  [InlineData(1)]
  [InlineData(8)]
  [InlineData(12)]
  [InlineData(14)]
  [InlineData(16)]
  public void BuildFromSource_StreamingPersistence_MatchesSerialSnapshotAtConfiguredDop(int maxDegreeOfParallelism)
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-streaming-shard-dop-tests", Guid.NewGuid().ToString("N"));
    try
    {
      const string source = """
        class Example
        {
          int First(int value) { if (value > 0) return Second(value); return Third(value); }
          int Second(int value) { var total = 0; for (var index = 0; index < value; index++) total += index; return total; }
          int Third(int value) => value + 3;
        }
        """;
      var serial = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        MaxDegreeOfParallelism = 1,
      }).BuildFromSource(source, "input.cs");
      var streaming = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        MaxDegreeOfParallelism = maxDegreeOfParallelism,
        Persistence = new CpgPersistenceOptions(root, $"streaming-dop-{maxDegreeOfParallelism}", StreamingMode: true),
      }).BuildFromSource(source, "input.cs");

      Assert.Equal(serial.GraphSnapshotVersion, streaming.GraphSnapshotVersion);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void BuildFromSource_PersistenceEnabled_PublishesSkeletonAndMethodShards()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-shard-build-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(root, "test-profile"),
      };
      var builder = new RoslynCpgBuilder(options);

      var graph = builder.BuildFromSource("""
        class Example
        {
          int First() => 1;
          int Second() => 2;
        }
        """, "input.cs");

      Assert.True(graph.HasQueryIndex);
      Assert.True(Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories).Count() >= 3);
      Assert.True(File.Exists(Path.Combine(root, "catalog.db")));

      var restoredBuilder = new RoslynCpgBuilder(options);
      var restored = restoredBuilder.BuildFromSource("""
        class Example
        {
          int First() => 1;
          int Second() => 2;
        }
        """, "input.cs");

      Assert.Equal(graph.GraphSnapshotVersion, restored.GraphSnapshotVersion);
      Assert.Equal(
        graph.Nodes.OrderBy(node => node.NodeId).Select(node => node.NodeId),
        restored.Nodes.OrderBy(node => node.NodeId).Select(node => node.NodeId));
      CpgExecutionSnapshotComparer.AssertEquivalent(
        CreateGraphSnapshot(graph),
        CreateGraphSnapshot(restored));
      Assert.Empty(restoredBuilder.LastBuildTelemetry.ExecutedPassNames!);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void BuildFromSource_CompleteShardWasRemoved_RebuildsInsteadOfFailing()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-shard-recovery-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with { Persistence = new CpgPersistenceOptions(root, "test-profile") };
      _ = new RoslynCpgBuilder(options).BuildFromSource("class Example { int Get() => 1; }", "input.cs");
      foreach (var shardPath in Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories))
      {
        File.Delete(shardPath);
      }

      var builder = new RoslynCpgBuilder(options);
      var graph = builder.BuildFromSource("class Example { int Get() => 1; }", "input.cs");

      Assert.True(graph.HasQueryIndex);
      Assert.NotEmpty(builder.LastBuildTelemetry.ExecutedPassNames!);
    }
    finally
    {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  private static CpgExecutionSnapshot CreateGraphSnapshot(RoslynCpgGraph graph)
  {
    return new CpgExecutionSnapshot(
      graph.GraphSnapshotVersion,
      graph.Nodes.Select(node =>
        $"{node.NodeId}:{node.Kind}:{node.DisplayKind}:{node.FilePath}:{node.SpanStart}:{node.SpanEnd}").ToArray(),
      graph.Edges.Select(edge =>
        $"{edge.SourceNodeId}>{edge.TargetNodeId}:{edge.Kind}:{edge.ContextId}").ToArray(),
      [],
      [],
      [],
      [],
      string.Empty,
      string.Empty);
  }

  private static string CreateOutOfOrderOperationFragmentSource()
  {
    var builder = new StringBuilder();
    builder.AppendLine("class Example");
    builder.AppendLine("{");
    builder.AppendLine("  int First(int seed)");
    builder.AppendLine("  {");
    builder.AppendLine("    var total = seed;");
    for (var index = 0; index < 4000; index++)
    {
      builder.Append("    total += ").Append(index).AppendLine(";");
    }

    builder.AppendLine("    return total;");
    builder.AppendLine("  }");
    builder.AppendLine("  int Second() => 2;");
    builder.AppendLine("}");
    return builder.ToString();
  }

  private static string CreateReuseFixtureSource(int changedLiteral)
  {
    var builder = new StringBuilder();
    builder.AppendLine("class Reuse");
    builder.AppendLine("{");
    builder.AppendLine("  int Method0(int value) => value;");
    for (var index = 0; index < 96; index += 1)
    {
      var previous = Math.Max(0, index - 1);
      var addend = index == 95 ? changedLiteral : index;
      builder.Append("  int Method").Append(index).Append("(int value) { var current = value + ")
        .Append(addend).Append("; if (current > ").Append(index / 2)
        .Append(") current += Method").Append(previous)
        .AppendLine("(current - 1); return current; }");
    }

    builder.AppendLine("}");
    return builder.ToString();
  }

  private static async Task<int[]> ReadCompletedOperationFragmentStageOrderAsync(string catalogPath)
  {
    await using var connection = new SqliteConnection(
      new SqliteConnectionStringBuilder
      {
        DataSource = catalogPath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false,
      }.ToString());
    await connection.OpenAsync(CancellationToken.None);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT f.span_start
      FROM session_shards s
      JOIN session_fragments f ON f.build_id = s.build_id AND f.fragment_id = s.fragment_id
      JOIN build_sessions b ON b.build_id = s.build_id
      WHERE b.status = 'Complete' AND f.fragment_kind = 'operation-fragment'
      ORDER BY s.rowid;
      """;
    var result = new List<int>();
    await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
    while (await reader.ReadAsync(CancellationToken.None))
    {
      result.Add(reader.GetInt32(0));
    }

    return result.ToArray();
  }
}
