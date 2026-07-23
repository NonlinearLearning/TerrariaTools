using MinimalRoslynCpg.Persistence;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgShardContractTests
{
  [Fact]
  public async Task WriteAsync_ValidShard_WritesReadableCompleteShard()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var store = new CpgShardStore(root);
      var shard = CreateShard("source-a", "profile-a", "fragment-a");

      var result = await store.WriteAsync(shard, CancellationToken.None);
      var recovered = await store.ReadAsync(result.Location, CancellationToken.None);

      Assert.True(File.Exists(result.Location.ShardPath));
      Assert.Equal(CpgShardStatus.Complete, result.Location.Status);
      Assert.Equal(shard.Nodes, recovered.Nodes);
      Assert.Equal(shard.Edges, recovered.Edges);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task WriteAsync_BoundaryEdgeManifest_PreservesGlobalNodeIds()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var store = new CpgShardStore(root);
      var shard = CreateShard("source-a", "profile-a", "boundary-manifest") with
      {
        BoundaryEdges = new[]
        {
          new CpgFrozenBoundaryEdge(7, 99, "InterproceduralDataFlow", null, "callsite:input.cs:0:10:Run"),
        },
      };

      var result = await store.WriteAsync(shard, CancellationToken.None);
      var recovered = await store.ReadAsync(result.Location, CancellationToken.None);

      var boundary = Assert.Single(recovered.BoundaryEdges!);
      Assert.Equal((uint)7, boundary.SourceNodeId);
      Assert.Equal((uint)99, boundary.TargetNodeId);
      Assert.Equal("InterproceduralDataFlow", boundary.Kind);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task WriteAsync_Strict_WhenTemporaryShardIsMutated_ThrowsAndDoesNotPublish()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      using var hook = InstallAfterWriteHook(path =>
      {
        if (IsWithinDirectory(path, root))
        {
          File.AppendAllText(path, "x");
        }
      });
      var store = new CpgShardStore(root);

      await Assert.ThrowsAsync<InvalidDataException>(() => store.WriteAsync(
        CreateShard("source-a", "profile-a", "fragment-a"),
        CancellationToken.None));

      Assert.Empty(Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories));
      Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task WriteAsync_Throughput_WhenTemporaryShardIsMutated_DefersFullValidationToRead()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      using var hook = InstallAfterWriteHook(path =>
      {
        if (IsWithinDirectory(path, root))
        {
          File.AppendAllText(path, "x");
        }
      });
      var store = new CpgShardStore(root, CpgPersistenceDurabilityMode.Throughput);

      var result = await store.WriteAsync(
        CreateShard("source-a", "profile-a", "fragment-a"),
        CancellationToken.None);

      Assert.True(File.Exists(result.Location.ShardPath));
      await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync(result.Location, CancellationToken.None));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task WriteAsync_Strict_WhenShardHasDuplicateLocalIndexes_ThrowsAndDoesNotPublish()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var shard = CreateShard("source-a", "profile-a", "fragment-a") with
      {
        Nodes = new[]
        {
          new CpgFrozenNode(0, 7, "Operation", "input.cs", 0, 5, "M", null, null, null, false),
          new CpgFrozenNode(0, 9, "Operation", "input.cs", 5, 10, "M", null, null, null, false),
        },
        Edges = Array.Empty<CpgFrozenEdge>(),
      };
      var store = new CpgShardStore(root);

      await Assert.ThrowsAsync<InvalidDataException>(() => store.WriteAsync(shard, CancellationToken.None));

      Assert.Empty(Directory.EnumerateFiles(root, "*.cpgbin", SearchOption.AllDirectories));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Export_FrozenGraph_PreservesOrderedNodeIdsAndEdges()
  {
    var graph = new RoslynCpgGraph();
    var first = graph.AddNode(new RoslynCpgNode(RoslynCpgNodeKind.Operation, "Operation", Name: "first"));
    var second = graph.AddNode(new RoslynCpgNode(RoslynCpgNodeKind.Operation, "Operation", Name: "second"));
    graph.AddEdge(first, second, RoslynCpgEdgeKind.DataFlow);
    graph.FreezeQueryIndex();

    var shard = CpgFrozenShardExporter.Export(graph, CreateShard("source-a", "profile-a", "fragment-a").Lookup);

    Assert.Equal(graph.Nodes.OrderBy(node => node.NodeId).Select(node => node.NodeId!.Value.Value), shard.Nodes.Select(node => node.NodeId));
    Assert.Equal("DataFlow", Assert.Single(shard.Edges).Kind);
  }

  [Fact]
  public void Export_MutableGraph_Throws()
  {
    var graph = new RoslynCpgGraph();
    graph.AddNode(new RoslynCpgNode(RoslynCpgNodeKind.Operation, "Operation", Name: "node"));

    Assert.Throws<InvalidOperationException>(() => CpgFrozenShardExporter.Export(graph, CreateShard("source-a", "profile-a", "fragment-a").Lookup));
  }

  [Fact]
  public void ExportDescriptors_PreallocatedLocalNodes_ExportsLocalEdgesAndReturnsBoundaryEdges()
  {
    var firstAnchor = CreateOperationAnchor(spanStart: 1, spanEnd: 2);
    var secondAnchor = CreateOperationAnchor(spanStart: 3, spanEnd: 4);
    var externalAnchor = CreateOperationAnchor(spanStart: 5, spanEnd: 6);
    var allocation = DeterministicNodeIdTable.Create(new[] { firstAnchor, secondAnchor, externalAnchor });
    var streamingAssembly = typeof(CpgFrozenShardExporter).Assembly;
    var descriptorType = streamingAssembly.GetType("MinimalRoslynCpg.Builder.Streaming.CpgNodeDescriptor");
    var candidateType = streamingAssembly.GetType("MinimalRoslynCpg.Builder.Streaming.CpgEdgeCandidate");
    Assert.NotNull(descriptorType);
    Assert.NotNull(candidateType);
    var descriptors = Array.CreateInstance(descriptorType!, 2);
    descriptors.SetValue(CreateOperationDescriptor(descriptorType, firstAnchor, "first"), 0);
    descriptors.SetValue(CreateOperationDescriptor(descriptorType, secondAnchor, "second"), 1);
    var candidates = Array.CreateInstance(candidateType!, 2);
    candidates.SetValue(CreateCandidate(candidateType, firstAnchor, secondAnchor, RoslynCpgEdgeKind.DataFlow), 0);
    candidates.SetValue(CreateCandidate(candidateType, secondAnchor, externalAnchor, RoslynCpgEdgeKind.CallTargets), 1);
    var boundaryEdges = new List<CpgFrozenBoundaryEdge>();
    var method = typeof(CpgFrozenShardExporter).GetMethod(
      "ExportDescriptors",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

    Assert.NotNull(method);
    var shard = Assert.IsType<CpgFrozenShard>(method!.Invoke(
      null,
      new object[]
      {
        CreateShard("source-a", "profile-a", "fragment-a").Lookup,
        descriptors,
        candidates,
        allocation,
        boundaryEdges,
      }));

    var expectedAllocation = DeterministicNodeIdTable.Create(new[] { firstAnchor, secondAnchor });
    var graph = new RoslynCpgGraph(expectedAllocation);
    var first = graph.AddNode(CreateOperationNode(firstAnchor, "first", expectedAllocation));
    var second = graph.AddNode(CreateOperationNode(secondAnchor, "second", expectedAllocation));
    graph.AddEdge(first, second, RoslynCpgEdgeKind.DataFlow);
    graph.FreezeQueryIndex();
    var expected = CpgFrozenShardExporter.Export(graph, shard.Lookup);

    Assert.Equal(expected.Nodes, shard.Nodes);
    Assert.Equal(expected.Edges, shard.Edges);
    var boundaryEdge = Assert.Single(boundaryEdges);
    Assert.Equal(allocation.GetRequiredId(secondAnchor).Value, boundaryEdge.SourceNodeId);
    Assert.Equal(allocation.GetRequiredId(externalAnchor).Value, boundaryEdge.TargetNodeId);
    Assert.Equal("CallTargets", boundaryEdge.Kind);
  }

  [Fact]
  public void Commit_OperationFragmentFacts_ExportsOnceAndReleasesFacts()
  {
    var firstAnchor = CreateOperationAnchor(spanStart: 1, spanEnd: 2);
    var secondAnchor = CreateOperationAnchor(spanStart: 3, spanEnd: 4);
    var allocation = DeterministicNodeIdTable.Create(new[] { firstAnchor, secondAnchor });
    var assembly = typeof(CpgFrozenShardExporter).Assembly;
    var descriptorType = assembly.GetType("MinimalRoslynCpg.Builder.Streaming.CpgNodeDescriptor");
    var candidateType = assembly.GetType("MinimalRoslynCpg.Builder.Streaming.CpgEdgeCandidate");
    var factsType = assembly.GetType("MinimalRoslynCpg.Builder.Streaming.OperationFragmentFacts");
    var committerType = assembly.GetType("MinimalRoslynCpg.Builder.Streaming.StreamingFragmentCommitter");
    Assert.NotNull(descriptorType);
    Assert.NotNull(candidateType);
    Assert.NotNull(factsType);
    Assert.NotNull(committerType);
    var descriptors = Array.CreateInstance(descriptorType!, 2);
    descriptors.SetValue(CreateOperationDescriptor(descriptorType, firstAnchor, "first"), 0);
    descriptors.SetValue(CreateOperationDescriptor(descriptorType, secondAnchor, "second"), 1);
    var candidates = Array.CreateInstance(candidateType!, 1);
    candidates.SetValue(CreateCandidate(candidateType, firstAnchor, secondAnchor, RoslynCpgEdgeKind.DataFlow), 0);
    var facts = factsType!.GetConstructors(
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
      .Single()
      .Invoke(new object?[] { 0, 0, 10, 0, 10, null, descriptors, candidates });
    var commit = committerType!.GetMethod(
      "Commit",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(commit);
    var boundaries = new List<CpgFrozenBoundaryEdge>();

    var shard = Assert.IsType<CpgFrozenShard>(commit!.Invoke(
      null,
      new object[] { CreateShard("source-a", "profile-a", "fragment-a").Lookup, facts!, allocation, boundaries }));

    Assert.Equal(2, shard.Nodes.Count);
    var nodeDescriptors = factsType.GetProperty(
      "NodeDescriptors",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(facts);
    Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(nodeDescriptors).Cast<object>());
    var retry = Assert.Throws<System.Reflection.TargetInvocationException>(() => commit.Invoke(
      null,
      new object[] { CreateShard("source-a", "profile-a", "fragment-a").Lookup, facts, allocation, boundaries }));
    Assert.IsType<InvalidOperationException>(retry.InnerException);
  }

  [Fact]
  public void Create_CrossShardEdge_PreservesGlobalNodeIdsAndCallSiteContext()
  {
    var assembly = typeof(CpgFrozenShardExporter).Assembly;
    var committerType = assembly.GetType("MinimalRoslynCpg.Builder.Streaming.CrossShardEdgeCommitter");
    Assert.NotNull(committerType);
    var create = committerType!.GetMethod(
      "Create",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(create);
    var context = new RoslynCpgCallSiteContext("input.cs", 3, 9, "Run");
    var edge = new RoslynCpgEdge(new NodeId(2), new NodeId(7), RoslynCpgEdgeKind.CallTargets, callSiteContext: context);

    var boundary = Assert.IsType<CpgFrozenBoundaryEdge>(create!.Invoke(null, new object[] { edge }));

    Assert.Equal((uint)2, boundary.SourceNodeId);
    Assert.Equal((uint)7, boundary.TargetNodeId);
    Assert.Equal("input.cs", boundary.CallSiteFilePath);
    Assert.Equal(3, boundary.CallSiteSpanStart);
  }

  [Theory]
  [InlineData("source-b", "profile-a", "fragment-a")]
  [InlineData("source-a", "profile-b", "fragment-a")]
  [InlineData("source-a", "profile-a", "fragment-b")]
  public async Task TryReadAsync_IdentityHashDoesNotMatch_ReturnsNull(
    string sourceHash,
    string profileHash,
    string fragmentHash)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var store = new CpgShardStore(root);
      var result = await store.WriteAsync(CreateShard("source-a", "profile-a", "fragment-a"), CancellationToken.None);
      var lookup = new CpgShardLookup(
        new CpgFileKey("project", "input.cs", sourceHash),
        new CpgFragmentKey("method", 0, 10, fragmentHash),
        SchemaVersion: 1,
        profileHash);

      var recovered = await store.TryReadAsync(result.Location, lookup, CancellationToken.None);

      Assert.Null(recovered);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static CpgFrozenShard CreateShard(string sourceHash, string profileHash, string fragmentHash)
  {
    var lookup = new CpgShardLookup(
      new CpgFileKey("project", "input.cs", sourceHash),
      new CpgFragmentKey("method", 0, 10, fragmentHash),
      SchemaVersion: 1,
      profileHash);
    return new CpgFrozenShard(
      lookup,
      new[] { new CpgFrozenNode(0, 7, "Operation", "input.cs", 0, 10, "M", null, null, null, false) },
      new[] { new CpgFrozenEdge(0, 0, "DataFlow", null, null) },
      Array.Empty<CpgSymbolLocation>());
  }

  private static StableNodeAnchor CreateOperationAnchor(int spanStart, int spanEnd)
  {
    return new StableNodeAnchor(
      RoslynCpgNodeKind.Operation,
      FilePathId: 1,
      spanStart,
      spanEnd,
      StableNodeRole.Operation,
      Ordinal: 0,
      ExtraKeyId: (uint)spanStart);
  }

  private static object CreateOperationDescriptor(Type descriptorType, StableNodeAnchor anchor, string name)
  {
    return Activator.CreateInstance(
      descriptorType,
      anchor,
      RoslynCpgNodeKind.Operation,
      "Operation",
      name,
      null,
      null,
      null,
      null,
      "input.cs",
      anchor.SpanStart,
      anchor.SpanEnd,
      false)!;
  }

  private static object CreateCandidate(
    Type candidateType,
    StableNodeAnchor sourceAnchor,
    StableNodeAnchor targetAnchor,
    RoslynCpgEdgeKind kind)
  {
    return Activator.CreateInstance(candidateType, sourceAnchor, targetAnchor, kind, null, null, null)!;
  }

  private static RoslynCpgNode CreateOperationNode(
    StableNodeAnchor anchor,
    string name,
    DeterministicNodeIdTable allocation)
  {
    return new RoslynCpgNode(
      RoslynCpgNodeKind.Operation,
      "Operation",
      Name: name,
      FilePath: "input.cs",
      SpanStart: anchor.SpanStart,
      SpanEnd: anchor.SpanEnd,
      IsImplicit: false,
      NodeId: allocation.GetRequiredId(anchor),
      StableAnchor: anchor);
  }

  private static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(Path.GetTempPath(), "cpg-shard-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
  }

  private static IDisposable InstallAfterWriteHook(Action<string> hook)
  {
    var property = typeof(CpgShardStore).GetProperty(
      "AfterTemporaryWriteForTesting",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(property);
    property!.SetValue(null, hook);
    return new DelegateDisposable(() => property.SetValue(null, null));
  }

  private static bool IsWithinDirectory(string path, string directory)
  {
    var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(
      Path.DirectorySeparatorChar,
      Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return Path.GetFullPath(path).StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
  }

  private sealed class DelegateDisposable(Action dispose) : IDisposable
  {
    public void Dispose()
    {
      dispose.Invoke();
    }
  }
}
