using MinimalRoslynCpg.Persistence;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgBuildRoutingIndexTests
{
  [Fact]
  public async Task WriteAsync_ReadAsync_RoundTripsLookupsAndDeduplicatesRoutes()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var file = new CpgFileKey("project", "input.cs", "source");
      var primaryZLookup = new CpgShardLookup(
        file,
        new CpgFragmentKey("method-z", 0, 20, "fragment-z"),
        1,
        "profile");
      var primaryALookup = new CpgShardLookup(
        file,
        new CpgFragmentKey("method-a", 20, 20, "fragment-a"),
        1,
        "profile");
      var boundaryLookup = new CpgShardLookup(
        file,
        new CpgFragmentKey("boundary-adjacency", 40, 10, "boundary"),
        1,
        "profile");
      var entries = new[]
      {
        new CpgBuildRoutingShardEntry(
          CreateBoundaryShard(boundaryLookup),
          CreateLocation(root, "boundary-m")),
        new CpgBuildRoutingShardEntry(
          CreatePrimaryShard(primaryZLookup, "alpha", 7, 0, 10),
          CreateLocation(root, "shard-z")),
        new CpgBuildRoutingShardEntry(
          CreatePrimaryShard(primaryALookup, "alpha", 8, 0, 10),
          CreateLocation(root, "shard-a")),
        new CpgBuildRoutingShardEntry(
          CreatePrimaryShard(primaryZLookup, "alpha", 7, 0, 10),
          CreateLocation(root, "shard-z")),
      };
      var path = Path.Combine(root, "builds", "build-1", "routing.cpgidx");
      var writer = new CpgBuildRoutingIndexWriter();
      var writeResult = await writer.WriteAsync(path, "build-1", entries, CancellationToken.None);

      Assert.Equal(path, writeResult.IndexPath);
      Assert.True(File.Exists(path));
      Assert.False(File.Exists(path + ".tmp"));

      var index = await new CpgBuildRoutingIndexReader().ReadAsync(path, CancellationToken.None);

      Assert.Equal("build-1", index.BuildId);
      Assert.Equal(1, index.SchemaVersion);
      Assert.Equal("profile", index.ProfileHash);
      Assert.Equal(writeResult.PayloadHash, index.PayloadHash);
      Assert.Equal(
        new[] { new CpgBuildRoutingPrimaryNodeRoute(7, "shard-z", 0) },
        index.FindPrimaryNode(7));
      Assert.Empty(index.FindPrimaryNode(999));
      Assert.Equal(
        new[] { new CpgBuildRoutingBoundaryNodeRoute(7, "boundary-m") },
        index.FindBoundaryNode(7));
      Assert.Equal(
        new[] { new CpgBuildRoutingBoundaryNodeRoute(99, "boundary-m") },
        index.FindBoundaryNode(99));
      Assert.Equal(
        new[]
        {
          new CpgBuildRoutingSpanRoute(file, 0, 10, "shard-a"),
          new CpgBuildRoutingSpanRoute(file, 0, 10, "shard-z"),
        },
        index.FindBySpan(new CpgSpanLookup(file, 0, 10)));
      Assert.Equal(
        new[]
        {
          new CpgBuildRoutingSymbolRoute("alpha", "shard-a", 0),
          new CpgBuildRoutingSymbolRoute("alpha", "shard-z", 0),
        },
        index.FindBySymbol(new CpgSymbolLookup("alpha")));
      Assert.Empty(index.FindBySpan(new CpgSpanLookup(file, 100, 5)));
      Assert.Empty(index.FindBySymbol(new CpgSymbolLookup("missing")));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task WriteAsync_SameLogicalEntriesInDifferentOrder_ProducesIdenticalBytes()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var file = new CpgFileKey("project", "input.cs", "source");
      var firstLookup = new CpgShardLookup(
        file,
        new CpgFragmentKey("method-b", 20, 20, "fragment-b"),
        1,
        "profile");
      var secondLookup = new CpgShardLookup(
        file,
        new CpgFragmentKey("method-a", 0, 20, "fragment-a"),
        1,
        "profile");
      var firstEntries = new[]
      {
        new CpgBuildRoutingShardEntry(
          CreatePrimaryShard(firstLookup, "beta", 20, 20, 30),
          CreateLocation(root, "shard-b")),
        new CpgBuildRoutingShardEntry(
          CreatePrimaryShard(secondLookup, "alpha", 10, 0, 10),
          CreateLocation(root, "shard-a")),
      };
      var secondEntries = firstEntries.Reverse().ToArray();
      var firstPath = Path.Combine(root, "builds", "first", "routing.cpgidx");
      var secondPath = Path.Combine(root, "builds", "second", "routing.cpgidx");
      var writer = new CpgBuildRoutingIndexWriter();

      var first = await writer.WriteAsync(firstPath, "same-build", firstEntries, CancellationToken.None);
      var second = await writer.WriteAsync(secondPath, "same-build", secondEntries, CancellationToken.None);

      Assert.Equal(first.PayloadHash, second.PayloadHash);
      Assert.Equal(await File.ReadAllBytesAsync(firstPath), await File.ReadAllBytesAsync(secondPath));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task ReadAsync_TruncatedFile_ThrowsInvalidDataException()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var file = new CpgFileKey("project", "input.cs", "source");
      var lookup = new CpgShardLookup(
        file,
        new CpgFragmentKey("method", 0, 20, "fragment"),
        1,
        "profile");
      var path = Path.Combine(root, "builds", "build-1", "routing.cpgidx");
      await new CpgBuildRoutingIndexWriter().WriteAsync(
        path,
        "build-1",
        new[]
        {
          new CpgBuildRoutingShardEntry(
            CreatePrimaryShard(lookup, "alpha", 7, 0, 10),
            CreateLocation(root, "shard-a")),
        },
        CancellationToken.None);
      var bytes = await File.ReadAllBytesAsync(path);
      await File.WriteAllBytesAsync(path, bytes[..^1]);

      var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
        new CpgBuildRoutingIndexReader().ReadAsync(path, CancellationToken.None));

      Assert.Contains("routing index", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task ReadAsync_UnsupportedVersion_ThrowsInvalidDataException()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var file = new CpgFileKey("project", "input.cs", "source");
      var lookup = new CpgShardLookup(
        file,
        new CpgFragmentKey("method", 0, 20, "fragment"),
        1,
        "profile");
      var path = Path.Combine(root, "builds", "build-1", "routing.cpgidx");
      await new CpgBuildRoutingIndexWriter().WriteAsync(
        path,
        "build-1",
        new[]
        {
          new CpgBuildRoutingShardEntry(
            CreatePrimaryShard(lookup, "alpha", 7, 0, 10),
            CreateLocation(root, "shard-a")),
        },
        CancellationToken.None);
      var bytes = await File.ReadAllBytesAsync(path);
      bytes[4] = 0x7f;
      await File.WriteAllBytesAsync(path, bytes);

      var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
        new CpgBuildRoutingIndexReader().ReadAsync(path, CancellationToken.None));

      Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task ReadAsync_PayloadHashMismatch_ThrowsInvalidDataException()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var file = new CpgFileKey("project", "input.cs", "source");
      var lookup = new CpgShardLookup(
        file,
        new CpgFragmentKey("method", 0, 20, "fragment"),
        1,
        "profile");
      var path = Path.Combine(root, "builds", "build-1", "routing.cpgidx");
      await new CpgBuildRoutingIndexWriter().WriteAsync(
        path,
        "build-1",
        new[]
        {
          new CpgBuildRoutingShardEntry(
            CreatePrimaryShard(lookup, "alpha", 7, 0, 10),
            CreateLocation(root, "shard-a")),
        },
        CancellationToken.None);
      var bytes = await File.ReadAllBytesAsync(path);
      bytes[^1] ^= 0x01;
      await File.WriteAllBytesAsync(path, bytes);

      var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
        new CpgBuildRoutingIndexReader().ReadAsync(path, CancellationToken.None));

      Assert.Contains("hash", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static CpgFrozenShard CreatePrimaryShard(
    CpgShardLookup lookup,
    string symbolKey,
    uint nodeId,
    int spanStart,
    int spanEnd)
  {
    return new CpgFrozenShard(
      lookup,
      new[]
      {
        new CpgFrozenNode(
          0,
          nodeId,
          "Operation",
          lookup.File.RelativePath,
          spanStart,
          spanEnd,
          "Operation",
          $"node-{nodeId}",
          null,
          null,
          false),
      },
      Array.Empty<CpgFrozenEdge>(),
      new[]
      {
        new CpgSymbolLocation(symbolKey, 0),
        new CpgSymbolLocation(symbolKey, 0),
      });
  }

  private static CpgFrozenShard CreateBoundaryShard(CpgShardLookup lookup)
  {
    return new CpgFrozenShard(
      lookup,
      Array.Empty<CpgFrozenNode>(),
      Array.Empty<CpgFrozenEdge>(),
      Array.Empty<CpgSymbolLocation>(),
      new[]
      {
        new CpgFrozenBoundaryEdge(7, 99, "CallTargets", null, null),
        new CpgFrozenBoundaryEdge(7, 99, "CallTargets", null, null),
      },
      CpgShardRole.BoundaryAdjacency,
      new CpgBoundaryAdjacency("owner-fragment", CpgBoundaryAdjacencyDirection.Outgoing));
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

  private static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(Path.GetTempPath(), "cpg-build-routing-index-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
  }
}
