using NLCPG.Analysis.FlowSummaries;
using NLCPG.Persistence;
using NLCPG.Contracts;
using NLCPG.Builder;
using NLCPG.Model;
using System.Security.Cryptography;
using System.Text;
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
      var recovered = await store.ReadAsync(result, CancellationToken.None);

      Assert.True(File.Exists(result.ShardPath));
      Assert.Equal(CpgShardStatus.Complete, result.Status);
      Assert.Equal(7, ReadFormatVersion(result.ShardPath));
      Assert.NotNull(recovered.IncomingEdgeOffsets);
      Assert.NotNull(recovered.IncomingEdgeIndexes);
      Assert.Equal(recovered.Nodes.Count + 1, recovered.IncomingEdgeOffsets!.Count);
      Assert.Equal(recovered.Edges.Count, recovered.IncomingEdgeIndexes!.Count);
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
      var recovered = await store.ReadAsync(result, CancellationToken.None);

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
  public async Task WriteAsync_FlowSummaryBridge_PreservesTypedSummaryMetadata()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var store = new CpgShardStore(root);
      var label = new CpgFrozenFlowSummaryLabel(
        NLCPGInterproceduralBridgeKind.SummaryMapping,
        FlowSummaryResolution.Project,
        "Demo|Helpers|Map|0|1|value|global::System.String",
        FlowSummaryEndpoint.Parameter(0),
        FlowSummaryEndpoint.Return);
      var shard = CreateShard("source-a", "profile-a", "summary-metadata") with
      {
        Edges = new[]
        {
          new CpgFrozenEdge(
            0,
            0,
            "InterproceduralDataFlow",
            "interprocedural-summary:SummaryMapping:Project:Demo|Helpers|Map|0|1|value|global::System.String:Parameter { ParameterOrdinal = 0 }->Return { ParameterOrdinal = -1 }",
            null,
            FlowSummaryLabel: label),
        },
      };

      var location = await store.WriteAsync(shard, CancellationToken.None);
      var recovered = await store.ReadAsync(location, CancellationToken.None);
      var graph = CpgFrozenShardGraphReader.ReadGraph(recovered);

      var recoveredLabel = Assert.Single(recovered.Edges).FlowSummaryLabel;
      Assert.Equal(label, recoveredLabel);
      var restoredLabel = Assert.Single(graph.Edges).StructuredLabel;
      Assert.Equal(FlowSummaryResolution.Project, restoredLabel!.FlowSummaryResolution);
      Assert.Equal(label.MethodKey, restoredLabel.FlowSummaryMethodKey);
      Assert.Equal(label.Source, restoredLabel.FlowSummarySource);
      Assert.Equal(label.Target, restoredLabel.FlowSummaryTarget);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task ReadFromPathAsync_CorruptIncomingEdgeCsr_ThrowsInvalidDataException()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var store = new CpgShardStore(root);
      var shard = CreateProjectionShard();
      var result = await store.WriteAsync(shard, CancellationToken.None);
      var bytes = await File.ReadAllBytesAsync(result.ShardPath);
      var csrStart = bytes.Length - sizeof(int) * ((shard.Nodes.Count + 1) + shard.Edges.Count + 2);
      var invalidOffset = BitConverter.GetBytes(shard.Edges.Count + 1);
      Array.Copy(invalidOffset, 0, bytes, csrStart + sizeof(int), sizeof(int));
      await File.WriteAllBytesAsync(result.ShardPath, bytes);

      await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadFromPathAsync(
        result.ShardPath,
        CancellationToken.None));
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

      Assert.True(File.Exists(result.ShardPath));
      await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync(result, CancellationToken.None));
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
    var graph = new NLCPGGraph();
    var first = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "first"));
    var second = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "second"));
    graph.AddEdge(first, second, NLCPGEdgeKind.DataFlow);
    graph.FreezeQueryIndex();

    var shard = CpgFrozenShardExporter.Export(graph, CreateShard("source-a", "profile-a", "fragment-a").Lookup);

    Assert.Equal(graph.Nodes.OrderBy(node => node.NodeId).Select(node => node.NodeId!.Value.Value), shard.Nodes.Select(node => node.NodeId));
    Assert.Equal("DataFlow", Assert.Single(shard.Edges).Kind);
  }

  [Fact]
  public void Export_MutableGraph_Throws()
  {
    var graph = new NLCPGGraph();
    graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "node"));

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
    var descriptorType = streamingAssembly.GetType("NLCPG.Builder.Streaming.CpgNodeDescriptor");
    var candidateType = streamingAssembly.GetType("NLCPG.Builder.Streaming.CpgEdgeCandidate");
    Assert.NotNull(descriptorType);
    Assert.NotNull(candidateType);
    var stringInterner = new StringInterner();
    var descriptors = Array.CreateInstance(descriptorType!, 2);
    descriptors.SetValue(CreateOperationDescriptor(descriptorType, firstAnchor, "first", stringInterner), 0);
    descriptors.SetValue(CreateOperationDescriptor(descriptorType, secondAnchor, "second", stringInterner), 1);
    var candidates = Array.CreateInstance(candidateType!, 2);
    candidates.SetValue(CreateCandidate(candidateType, firstAnchor, secondAnchor, NLCPGEdgeKind.DataFlow), 0);
    candidates.SetValue(CreateCandidate(candidateType, secondAnchor, externalAnchor, NLCPGEdgeKind.CallTargets), 1);
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
         stringInterner,
       }));

    var expectedAllocation = DeterministicNodeIdTable.Create(new[] { firstAnchor, secondAnchor });
    var graph = new NLCPGGraph(expectedAllocation);
    var first = graph.AddNode(CreateOperationNode(firstAnchor, "first", expectedAllocation), nodeId: expectedAllocation.GetRequiredId(firstAnchor), stableAnchor: firstAnchor);
    var second = graph.AddNode(CreateOperationNode(secondAnchor, "second", expectedAllocation), nodeId: expectedAllocation.GetRequiredId(secondAnchor), stableAnchor: secondAnchor);
    graph.AddEdge(first, second, NLCPGEdgeKind.DataFlow);
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
    var descriptorType = assembly.GetType("NLCPG.Builder.Streaming.CpgNodeDescriptor");
    var candidateType = assembly.GetType("NLCPG.Builder.Streaming.CpgEdgeCandidate");
    var factsType = assembly.GetType("NLCPG.Builder.Streaming.OperationFragmentFacts");
    var committerType = assembly.GetType("NLCPG.Builder.Streaming.StreamingFragmentCommitter");
    Assert.NotNull(descriptorType);
    Assert.NotNull(candidateType);
    Assert.NotNull(factsType);
    Assert.NotNull(committerType);
    var stringInterner = new StringInterner();
    var descriptors = Array.CreateInstance(descriptorType!, 2);
    descriptors.SetValue(CreateOperationDescriptor(descriptorType, firstAnchor, "first", stringInterner), 0);
    descriptors.SetValue(CreateOperationDescriptor(descriptorType, secondAnchor, "second", stringInterner), 1);
    var candidates = Array.CreateInstance(candidateType!, 1);
    candidates.SetValue(CreateCandidate(candidateType, firstAnchor, secondAnchor, NLCPGEdgeKind.DataFlow), 0);
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
       new object[] { CreateShard("source-a", "profile-a", "fragment-a").Lookup, facts!, allocation, boundaries, stringInterner }));

    Assert.Equal(2, shard.Nodes.Count);
    var nodeDescriptors = factsType.GetProperty(
      "NodeDescriptors",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(facts);
    Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(nodeDescriptors).Cast<object>());
    var retry = Assert.Throws<System.Reflection.TargetInvocationException>(() => commit.Invoke(
      null,
       new object[] { CreateShard("source-a", "profile-a", "fragment-a").Lookup, facts, allocation, boundaries, stringInterner }));
    Assert.IsType<InvalidOperationException>(retry.InnerException);
  }

  [Fact]
  public void Create_CrossShardEdge_PreservesGlobalNodeIdsAndCallSiteContext()
  {
    var assembly = typeof(CpgFrozenShardExporter).Assembly;
    var committerType = assembly.GetType("NLCPG.Builder.Streaming.CrossShardEdgeCommitter");
    Assert.NotNull(committerType);
    var create = committerType!.GetMethod(
      "Create",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(create);
    var context = new NLCPGCallSiteContext("input.cs", 3, 9, "Run");
    var edge = new NLCPGEdge(new NodeId(2), new NodeId(7), NLCPGEdgeKind.CallTargets, callSiteContext: context);

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
  public async Task TryReadAsync_IdentityHashDoesNotMatch_ReturnsNull(string sourceHash, string profileHash, string fragmentHash)
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

      var recovered = await store.TryReadAsync(result, lookup, CancellationToken.None);

      Assert.Null(recovered);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task ReadAsync_LegacyV5Shard_RemainsReadable()
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var store = new CpgShardStore(root);
      var shard = CreateProjectionShard();
      var location = await WriteLegacyV5ShardAsync(root, shard);

      var recovered = await store.ReadAsync(location, CancellationToken.None);
      var projection = CpgFrozenShardGraphReader.ReadIncomingProjection(
        recovered,
        new NodeId(13),
        new HashSet<NLCPGEdgeKind> { NLCPGEdgeKind.DataFlow },
        maxEdges: 16);

      Assert.Null(recovered.IncomingEdgeOffsets);
      Assert.Null(recovered.IncomingEdgeIndexes);
      Assert.Equal(new[] { new NodeId(7), new NodeId(11), new NodeId(13) }, projection.Nodes.Keys.OrderBy(id => id).ToArray());
      Assert.Equal(
        new[] { new NodeId(7), new NodeId(11) },
        projection.IncomingEdges.Select(edge => edge.SourceNodeId).ToArray());
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

  private static CpgFrozenShard CreateProjectionShard()
  {
    var lookup = new CpgShardLookup(
      new CpgFileKey("project", "input.cs", "source-a"),
      new CpgFragmentKey("method", 0, 10, "projection-fragment"),
      SchemaVersion: 1,
      "profile-a");
    return new CpgFrozenShard(
      lookup,
      new[]
      {
        new CpgFrozenNode(0, 7, "Operation", "input.cs", 0, 2, "Operation", "source-a", null, null, false),
        new CpgFrozenNode(1, 11, "Operation", "input.cs", 3, 5, "Operation", "source-b", null, null, false),
        new CpgFrozenNode(2, 13, "Operation", "input.cs", 6, 8, "Operation", "sink", null, null, false),
      },
      new[]
      {
        new CpgFrozenEdge(0, 2, "DataFlow", null, null, "input.cs", 0, 2, "CallA"),
        new CpgFrozenEdge(1, 2, "DataFlow", null, "callsite:input.cs:3:5:CallB", "input.cs", 3, 5, "CallB"),
        new CpgFrozenEdge(0, 1, "DataFlow", null, null),
      },
      Array.Empty<CpgSymbolLocation>());
  }

  private static StableNodeAnchor CreateOperationAnchor(int spanStart, int spanEnd)
  {
    return new StableNodeAnchor(
      NLCPGNodeKind.Operation,
      FilePathId: 1,
      spanStart,
      spanEnd,
      StableNodeRole.Operation,
      Ordinal: 0,
      ExtraKeyId: (uint)spanStart);
  }

  private static object CreateOperationDescriptor(Type descriptorType, StableNodeAnchor anchor, string name, StringInterner stringInterner)
  {
    return Activator.CreateInstance(
      descriptorType,
      anchor,
      NLCPGNodeKind.Operation,
      stringInterner.Intern(name),
      0u,
      0u,
      null,
      0u,
      stringInterner.Intern("input.cs"),
      anchor.SpanStart,
      anchor.SpanEnd,
      false)!;
  }

  private static object CreateCandidate(Type candidateType, StableNodeAnchor sourceAnchor, StableNodeAnchor targetAnchor, NLCPGEdgeKind kind)
  {
    return Activator.CreateInstance(candidateType, sourceAnchor, targetAnchor, kind, null, null, null)!;
  }

  private static NLCPGNodeDraft CreateOperationNode(StableNodeAnchor anchor, string name, DeterministicNodeIdTable allocation)
  {
    return new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: name,
      FilePath: "input.cs",
      SpanStart: anchor.SpanStart,
      SpanEnd: anchor.SpanEnd);
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

  private static int ReadFormatVersion(string shardPath)
  {
    using var stream = new FileStream(shardPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
    _ = reader.ReadBytes(4);
    return reader.ReadInt32();
  }

  private static async Task<CpgShardLocation> WriteLegacyV5ShardAsync(string root, CpgFrozenShard shard)
  {
    var shardId = CreateShardId(shard.Lookup);
    var directory = Path.Combine(root, "shards", shardId[..2]);
    Directory.CreateDirectory(directory);
    var shardPath = Path.Combine(directory, $"{shardId}.cpgbin");
    var payload = SerializeLegacyV5(shard);
    await File.WriteAllBytesAsync(shardPath, payload);
    return new CpgShardLocation(
      shardId,
      shardPath,
      Convert.ToHexString(SHA256.HashData(payload)),
      payload.LongLength,
      CpgShardStatus.Complete);
  }

  private static string CreateShardId(CpgShardLookup lookup)
  {
    var identity = string.Join("|", lookup.File.ProjectId, lookup.File.RelativePath,
      lookup.File.SourceHash, lookup.Fragment.Kind, lookup.Fragment.SpanStart,
      lookup.Fragment.SpanLength, lookup.Fragment.FragmentHash, lookup.SchemaVersion,
      lookup.ProfileHash);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
  }

  private static byte[] SerializeLegacyV5(CpgFrozenShard shard)
  {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
    writer.Write("CPGB"u8.ToArray());
    writer.Write(5);
    WriteLookup(writer, shard.Lookup);
    writer.Write((int)shard.Role);
    WriteOptional(writer, shard.BoundaryAdjacency?.OwnerFragmentId);
    writer.Write((int?)shard.BoundaryAdjacency?.Direction ?? -1);
    writer.Write(shard.Nodes.Count);
    foreach (var node in shard.Nodes.OrderBy(node => node.LocalIndex))
    {
      writer.Write(node.LocalIndex);
      writer.Write(node.NodeId);
      WriteRequired(writer, node.Kind);
      WriteOptional(writer, node.FilePath);
      WriteOptionalInt(writer, node.SpanStart);
      WriteOptionalInt(writer, node.SpanEnd);
      WriteRequired(writer, node.DisplayKind);
      WriteOptional(writer, node.Name);
      WriteOptional(writer, node.FullName);
      WriteOptional(writer, node.Signature);
      writer.Write(node.IsImplicit);
      writer.Write(node.StableFilePathId);
      writer.Write(node.StableSpanStart);
      writer.Write(node.StableSpanEnd);
      writer.Write(node.StableRole);
      writer.Write(node.StableOrdinal);
      writer.Write(node.StableExtraKeyId);
    }

    writer.Write(shard.Edges.Count);
    foreach (var edge in shard.Edges)
    {
      writer.Write(edge.SourceLocalIndex);
      writer.Write(edge.TargetLocalIndex);
      WriteRequired(writer, edge.Kind);
      WriteOptional(writer, edge.Label);
      WriteOptional(writer, edge.ContextId);
      WriteOptional(writer, edge.CallSiteFilePath);
      WriteOptionalInt(writer, edge.CallSiteSpanStart);
      WriteOptionalInt(writer, edge.CallSiteSpanEnd);
      WriteOptional(writer, edge.CallSiteDisplayName);
    }

    var boundaryEdges = shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>();
    writer.Write(boundaryEdges.Count);
    foreach (var edge in boundaryEdges)
    {
      writer.Write(edge.SourceNodeId);
      writer.Write(edge.TargetNodeId);
      WriteRequired(writer, edge.Kind);
      WriteOptional(writer, edge.Label);
      WriteOptional(writer, edge.ContextId);
      WriteOptional(writer, edge.CallSiteFilePath);
      WriteOptionalInt(writer, edge.CallSiteSpanStart);
      WriteOptionalInt(writer, edge.CallSiteSpanEnd);
      WriteOptional(writer, edge.CallSiteDisplayName);
    }

    writer.Write(shard.SymbolLocations.Count);
    foreach (var location in shard.SymbolLocations.OrderBy(location => location.SymbolKey, StringComparer.Ordinal))
    {
      WriteRequired(writer, location.SymbolKey);
      writer.Write(location.LocalIndex);
    }

    writer.Flush();
    return stream.ToArray();
  }

  private static void WriteLookup(BinaryWriter writer, CpgShardLookup lookup)
  {
    WriteRequired(writer, lookup.File.ProjectId);
    WriteRequired(writer, lookup.File.RelativePath);
    WriteRequired(writer, lookup.File.SourceHash);
    WriteRequired(writer, lookup.Fragment.Kind);
    writer.Write(lookup.Fragment.SpanStart);
    writer.Write(lookup.Fragment.SpanLength);
    WriteRequired(writer, lookup.Fragment.FragmentHash);
    writer.Write(lookup.SchemaVersion);
    WriteRequired(writer, lookup.ProfileHash);
  }

  private static void WriteRequired(BinaryWriter writer, string value)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(value);
    writer.Write(value);
  }

  private static void WriteOptional(BinaryWriter writer, string? value)
  {
    writer.Write(value is not null);
    if (value is not null)
    {
      writer.Write(value);
    }
  }

  private static void WriteOptionalInt(BinaryWriter writer, int? value)
  {
    writer.Write(value.HasValue);
    if (value.HasValue)
    {
      writer.Write(value.Value);
    }
  }

  private sealed class DelegateDisposable(Action dispose) : IDisposable
  {
    public void Dispose()
    {
      dispose.Invoke();
    }
  }
}
