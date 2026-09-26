using Microsoft.CodeAnalysis;
using NLCPG.Analysis;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using NLCPG.Persistence;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgRelationQueryTests
{
    [Fact]
    public void RelationProfiles_DeclareOptionalSyntaxCapabilities()
    {
        Assert.Equal(
            NLCPGCapability.SyntaxSemantic | NLCPGCapability.SyntaxToken,
            CpgRelationProfiles.GetRequiredCapabilities(CpgRelationProfile.StructuralContainment));
        Assert.Equal(
            NLCPGCapability.SyntaxSemantic |
            NLCPGCapability.Reference |
            NLCPGCapability.TypeRef,
            CpgRelationProfiles.GetRequiredCapabilities(CpgRelationProfile.SemanticBinding));
        Assert.Contains(
            NLCPGEdgeKind.RefersToType,
            CpgRelationProfiles.GetAllowedEdgeKinds(CpgRelationProfile.SemanticBinding));
    }

    [Fact]
    public void Query_StructuralProfile_RejectsDataFlowConnector()
    {
        // Arrange
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.SyntaxNode, Name: "source"));
        var sink = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.SyntaxNode, Name: "sink"));
        graph.AddEdge(source, sink, NLCPGEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var service = new CpgRelationQueryService(graph);

        // Act
        var result = service.Query(CreateQuery(
            FindNodeId(graph, "source"),
            FindNodeId(graph, "sink"),
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Bidirectional));

        // Assert
        Assert.Equal(CpgQueryStatus.Disconnected, result.Status);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void Query_SameCompleteSpecification_ReturnsStableCachedResult()
    {
        // Arrange
        var graph = new NLCPGGraph();
        var parent = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.SyntaxNode, Name: "parent"));
        var child = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.SyntaxNode, Name: "child"));
        graph.AddEdge(parent, child, NLCPGEdgeKind.SyntaxChild);
        graph.FreezeQueryIndex();
        var service = new CpgRelationQueryService(graph);
        var query = CreateQuery(
            FindNodeId(graph, "parent"),
            FindNodeId(graph, "child"),
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Outgoing);

        // Act
        var first = service.Query(query);
        var second = service.Query(query);

        // Assert
        Assert.Equal(CpgQueryStatus.Complete, first.Status);
        Assert.Single(first.Paths);
        Assert.False(first.WasCacheHit);
        Assert.True(second.WasCacheHit);
        Assert.Equal(first.Paths, second.Paths);
    }

    [Fact]
    public void Query_MissingProfileCapability_ReturnsUnavailable()
    {
        // Arrange
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
        var sink = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "sink"));
        graph.AddEdge(source, sink, NLCPGEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var service = new CpgRelationQueryService(graph, NLCPGCapability.SyntaxSemantic);

        // Act
        var result = service.Query(CreateQuery(
            FindNodeId(graph, "source"),
            FindNodeId(graph, "sink"),
            CpgRelationProfile.LocalDataFlow,
            CpgQueryDirection.Outgoing));

        // Assert
        Assert.Equal(CpgQueryStatus.Unavailable, result.Status);
        Assert.Empty(result.Paths);
    }

    [Fact]
    public void Query_VisitedNodeBudget_ReturnsTruncated()
    {
        // Arrange
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.SyntaxNode, Name: "source"));
        var middle = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.SyntaxNode, Name: "middle"));
        var sink = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.SyntaxNode, Name: "sink"));
        graph.AddEdge(source, middle, NLCPGEdgeKind.SyntaxChild);
        graph.AddEdge(middle, sink, NLCPGEdgeKind.SyntaxChild);
        graph.FreezeQueryIndex();
        var service = new CpgRelationQueryService(graph);
        var query = CreateQuery(
            FindNodeId(graph, "source"),
            FindNodeId(graph, "sink"),
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Outgoing) with
        {
            Budget = new NLCPGTraversalBudget(4, 1, 1, 2, 16),
        };

        // Act
        var result = service.Query(query);

        // Assert
        Assert.Equal(CpgQueryStatus.Truncated, result.Status);
        Assert.Equal("maxVisitedNodes", result.TruncationReason);
    }

    [Fact]
    public async Task QueryAsync_FrozenShardAndMemory_ReturnSameStructuralResult()
    {
        // Arrange
        var shard = CreateStructuralShard();
        var graph = CpgFrozenShardGraphReader.ReadGraph(shard);
        var query = CreateQuery(new NodeId(1), new NodeId(2), CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Outgoing);
        var memory = new CpgRelationQueryService(graph).Query(query);
        var location = new CpgShardLocation("structural", "structural.cpgbin", "hash", 256, CpgShardStatus.Complete);
        var resolver = new CpgShardQueryResolver(new RelationQueryCatalog(location), new RelationQueryStore(shard), 1024);

        // Act
        var frozen = await new CpgShardRelationQueryService(resolver, NLCPGCapability.All)
            .QueryAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(memory.Status, frozen.Status);
        Assert.Equal(
            memory.Paths.Select(path => string.Join(",", path.NodeIds)).ToArray(),
            frozen.Paths.Select(path => string.Join(",", path.NodeIds)).ToArray());
        Assert.Equal(
            memory.Edges.Select(FormatEdge).ToArray(),
            frozen.Edges.Select(FormatEdge).ToArray());
        Assert.Equal(256, frozen.LoadedShardBytes);
    }

    [Fact]
    public async Task QueryAsync_BoundaryShardsAndMemory_ReturnSameStructuralResult()
    {
        // Arrange
        var sourceLookup = CreateLookup("source");
        var targetLookup = CreateLookup("target");
        var sourceShard = new CpgFrozenShard(
            sourceLookup,
            new[] { new CpgFrozenNode(0, 1, "SyntaxNode", "input.cs", 0, 1, "SyntaxNode", "source", null, null, false) },
            Array.Empty<CpgFrozenEdge>(),
            Array.Empty<CpgSymbolLocation>(),
            new[] { new CpgFrozenBoundaryEdge(1, 2, "SyntaxChild", null, null) });
        var targetShard = new CpgFrozenShard(
            targetLookup,
            new[] { new CpgFrozenNode(0, 2, "SyntaxNode", "input.cs", 1, 2, "SyntaxNode", "target", null, null, false) },
            Array.Empty<CpgFrozenEdge>(),
            Array.Empty<CpgSymbolLocation>());
        var sourceLocation = new CpgShardLocation("source", "source.cpgbin", "source", 128, CpgShardStatus.Complete);
        var targetLocation = new CpgShardLocation("target", "target.cpgbin", "target", 128, CpgShardStatus.Complete);
        var graph = NLCPGGraph.CreateFrozen(
            CpgFrozenShardGraphReader.ReadGraph(sourceShard).Nodes.Concat(CpgFrozenShardGraphReader.ReadGraph(targetShard).Nodes),
            new[] { new NLCPGEdge(new NodeId(1), new NodeId(2), NLCPGEdgeKind.SyntaxChild) });
        var query = CreateQuery(new NodeId(1), new NodeId(2), CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Outgoing);
        var memory = new CpgRelationQueryService(graph).Query(query);
        var resolver = new CpgShardQueryResolver(
            new MultiShardRelationQueryCatalog(sourceLocation, targetLocation),
            new MultiShardRelationQueryStore(sourceLocation, sourceShard, targetLocation, targetShard),
            1_024);

        // Act
        var frozen = await new CpgShardRelationQueryService(resolver, NLCPGCapability.All)
            .QueryAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(memory.Status, frozen.Status);
        Assert.Equal(memory.Paths.Select(path => string.Join(",", path.NodeIds)), frozen.Paths.Select(path => string.Join(",", path.NodeIds)));
        Assert.Equal(memory.Edges.Select(FormatEdge), frozen.Edges.Select(FormatEdge));
        Assert.Equal(256, frozen.LoadedShardBytes);
    }

    [Fact]
    public async Task QueryAsync_ShardsDoNotContainAnchor_ReturnsUnavailable()
    {
        // Arrange
        var shard = CreateStructuralShard();
        var location = new CpgShardLocation("missing", "missing.cpgbin", "hash", 256, CpgShardStatus.Complete);
        var resolver = new CpgShardQueryResolver(
            new RelationQueryCatalog(location, hasNode: false),
            new RelationQueryStore(shard),
            1024);
        var query = CreateQuery(new NodeId(1), new NodeId(2), CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Outgoing);

        // Act
        var result = await new CpgShardRelationQueryService(resolver, NLCPGCapability.All)
            .QueryAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(CpgQueryStatus.Unavailable, result.Status);
        Assert.Contains(result.UnavailableShards, unavailable => unavailable.NodeId == new NodeId(1));
    }

    [Fact]
    public async Task QueryAsync_LoadedShardByteBudget_ReturnsTruncatedBeforeOpeningShard()
    {
        // Arrange
        var shard = CreateStructuralShard();
        var location = new CpgShardLocation("structural", "structural.cpgbin", "hash", 256, CpgShardStatus.Complete);
        var resolver = new CpgShardQueryResolver(new RelationQueryCatalog(location), new RelationQueryStore(shard), 1024);
        var query = CreateQuery(new NodeId(1), new NodeId(2), CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Outgoing) with
        {
            Budget = new NLCPGTraversalBudget(4, 1, 1, 16, 16, MaxLoadedShardBytes: 255),
        };

        // Act
        var result = await new CpgShardRelationQueryService(resolver, NLCPGCapability.All)
            .QueryAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(CpgQueryStatus.Truncated, result.Status);
        Assert.Equal("maxLoadedShardBytes", result.TruncationReason);
        Assert.Equal(0, result.LoadedShardBytes);
    }

    [Fact]
    public void Query_BackwardSliceCallerFanout_ReturnsTruncated()
    {
        // Arrange
        var graph = new NLCPGGraph();
        var callerOne = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "callerOne"));
        var callerTwo = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "callerTwo"));
        var sink = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "sink"));
        graph.AddEdge(callerOne, sink, NLCPGEdgeKind.InterproceduralDataFlow);
        graph.AddEdge(callerTwo, sink, NLCPGEdgeKind.InterproceduralDataFlow);
        graph.FreezeQueryIndex();
        var query = CreateQuery(
            FindNodeId(graph, "sink"),
            FindNodeId(graph, "callerOne"),
            CpgRelationProfile.BackwardSlice,
            CpgQueryDirection.Incoming) with
        {
            Budget = new NLCPGTraversalBudget(4, 1, 1, 16, 16, MaxCallerFanout: 1, MaxCallDepth: 1),
        };

        // Act
        var result = new CpgRelationQueryService(graph).Query(query);

        // Assert
        Assert.Equal(CpgQueryStatus.Truncated, result.Status);
        Assert.Equal("maxCallerFanout", result.TruncationReason);
    }

    [Fact]
    public void Query_DopOneAndSixteen_ReturnsIdenticalStableResult()
    {
        // Arrange
        const string source = "public sealed class Sample { public int Run(Box box) { return box.Value + 1; } } public sealed class Box { public int Value { get; set; } }";
        const string filePath = "relation-query-dop.cs";
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single();
        var serial = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with { MaxDegreeOfParallelism = 1 })
            .BuildFromSource(source, filePath);
        var parallel = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with { MaxDegreeOfParallelism = 16 })
            .BuildFromSource(source, filePath);
        var serialQuery = CreateSyntaxQuery(serial, root, memberAccess);
        var parallelQuery = CreateSyntaxQuery(parallel, root, memberAccess);

        // Act
        var serialResult = new CpgRelationQueryService(serial).Query(serialQuery);
        var parallelResult = new CpgRelationQueryService(parallel).Query(parallelQuery);

        // Assert
        Assert.Equal(serialQuery.Source.NodeIds, parallelQuery.Source.NodeIds);
        Assert.Equal(serialQuery.Target!.NodeIds, parallelQuery.Target!.NodeIds);
        Assert.Equal(serialResult.Status, parallelResult.Status);
        Assert.Equal(
            serialResult.Paths.Select(path => string.Join(",", path.NodeIds)).ToArray(),
            parallelResult.Paths.Select(path => string.Join(",", path.NodeIds)).ToArray());
        Assert.Equal(
            serialResult.Edges.Select(FormatEdge).ToArray(),
            parallelResult.Edges.Select(FormatEdge).ToArray());
    }

    private static CpgRelationQuery CreateQuery(
        NodeId source,
        NodeId target,
        CpgRelationProfile profile,
        CpgQueryDirection direction)
    {
        return new CpgRelationQuery(
            profile,
            direction,
            new CpgNodeSelector(NodeIds: new HashSet<NodeId> { source }),
            new CpgNodeSelector(NodeIds: new HashSet<NodeId> { target }),
            new NLCPGTraversalBudget(4, 1, 1, 16, 16),
            CpgRelationProfiles.GetRequiredCapabilities(profile));
    }

    private static NodeId FindNodeId(NLCPGGraph graph, string name)
    {
        return graph.Nodes.Single(node => graph.ResolveName(node) == name).NodeId!.Value;
    }

    private static string FormatEdge(NLCPGEdge edge)
    {
        return $"{edge.Kind}|{edge.StructuredLabel?.StableKey}|{edge.SourceNodeId}|{edge.TargetNodeId}";
    }

    private static CpgRelationQuery CreateSyntaxQuery(
        NLCPGGraph graph,
        SyntaxNode root,
        MemberAccessExpressionSyntax memberAccess)
    {
        var source = graph.Nodes.Single(node =>
            node.Kind == NLCPGNodeKind.SyntaxNode &&
            node.SpanStart == root.SpanStart &&
            node.SpanEnd == root.Span.End).NodeId!.Value;
        var target = graph.Nodes.Single(node =>
            node.Kind == NLCPGNodeKind.SyntaxNode &&
            node.SpanStart == memberAccess.SpanStart &&
            node.SpanEnd == memberAccess.Span.End).NodeId!.Value;
        return new CpgRelationQuery(
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Outgoing,
            new CpgNodeSelector(NodeIds: new HashSet<NodeId> { source }),
            new CpgNodeSelector(NodeIds: new HashSet<NodeId> { target }),
            new NLCPGTraversalBudget(32, 1, 1, 4096, 8192),
            NLCPGCapability.SyntaxSemantic,
            CpgQueryPurpose.StructureView);
    }

    private static CpgFrozenShard CreateStructuralShard()
    {
        var lookup = new CpgShardLookup(
            new CpgFileKey("project", "input.cs", "source"),
            new CpgFragmentKey("method", 0, 10, "fragment"),
            1,
            "profile");
        return new CpgFrozenShard(
            lookup,
            new[]
            {
                new CpgFrozenNode(0, 1, "SyntaxNode", "input.cs", 0, 1, "SyntaxNode", "source", null, null, false),
                new CpgFrozenNode(1, 2, "SyntaxNode", "input.cs", 1, 2, "SyntaxNode", "sink", null, null, false),
            },
            new[] { new CpgFrozenEdge(0, 1, "SyntaxChild", null, null) },
            Array.Empty<CpgSymbolLocation>());
    }

    private static CpgShardLookup CreateLookup(string fragmentHash)
    {
        return new CpgShardLookup(
            new CpgFileKey("project", "input.cs", "source"),
            new CpgFragmentKey("method", 0, 10, fragmentHash),
            1,
            "profile");
    }

    private sealed class RelationQueryCatalog : ICpgShardCatalog
    {
        private readonly CpgShardLocation _location;
        private readonly bool _hasNode;

        public RelationQueryCatalog(CpgShardLocation location, bool hasNode = true)
        {
            _location = location;
            _hasNode = hasNode;
        }

        public Task<CpgShardLease?> TryAcquireAsync(CpgShardLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CpgReusableShardLease?> TryAcquireReusableAsync(CpgReusableFragmentKey key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindByFileAsync(CpgFileKey fileKey, int schemaVersion, string profileHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindByNodeAsync(uint nodeId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CpgShardLocation>>(_hasNode ? new[] { _location } : Array.Empty<CpgShardLocation>());
        public Task<IReadOnlyList<CpgShardLocation>> FindBySymbolAsync(CpgSymbolLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindBySpanAsync(CpgSpanLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PublishAsync(CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> BeginBuildAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StageAsync(string buildId, CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StageReusableAsync(string buildId, CpgShardLease lease, CpgFrozenShard shard, CpgReusableFragmentKey reusableKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteBuildAsync(string buildId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateBuildAsync(string buildId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateFileAsync(CpgFileKey fileKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RelationQueryStore : ICpgShardStore
    {
        private readonly CpgFrozenShard _shard;

        public RelationQueryStore(CpgFrozenShard shard)
        {
            _shard = shard;
        }

        public Task<CpgShardLocation> WriteAsync(CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CpgFrozenShard> ReadAsync(CpgShardLocation location, CancellationToken cancellationToken) => Task.FromResult(_shard);
        public Task<CpgFrozenShard?> TryReadAsync(CpgShardLocation location, CpgShardLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(CpgFrozenShard Shard, CpgShardLocation Location)> ReadFromPathAsync(string shardPath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MultiShardRelationQueryCatalog : ICpgShardCatalog
    {
        private readonly CpgShardLocation _source;
        private readonly CpgShardLocation _target;

        public MultiShardRelationQueryCatalog(CpgShardLocation source, CpgShardLocation target)
        {
            _source = source;
            _target = target;
        }

        public Task<CpgShardLease?> TryAcquireAsync(CpgShardLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CpgReusableShardLease?> TryAcquireReusableAsync(CpgReusableFragmentKey key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindByFileAsync(CpgFileKey fileKey, int schemaVersion, string profileHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindByNodeAsync(uint nodeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CpgShardLocation>>(nodeId == 1 ? new[] { _source } : nodeId == 2 ? new[] { _target } : Array.Empty<CpgShardLocation>());
        public Task<IReadOnlyList<CpgShardLocation>> FindBySymbolAsync(CpgSymbolLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindBySpanAsync(CpgSpanLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PublishAsync(CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> BeginBuildAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StageAsync(string buildId, CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StageReusableAsync(string buildId, CpgShardLease lease, CpgFrozenShard shard, CpgReusableFragmentKey reusableKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteBuildAsync(string buildId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateBuildAsync(string buildId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateFileAsync(CpgFileKey fileKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MultiShardRelationQueryStore : ICpgShardStore
    {
        private readonly IReadOnlyDictionary<string, CpgFrozenShard> _shards;

        public MultiShardRelationQueryStore(
            CpgShardLocation sourceLocation,
            CpgFrozenShard sourceShard,
            CpgShardLocation targetLocation,
            CpgFrozenShard targetShard)
        {
            _shards = new Dictionary<string, CpgFrozenShard>(StringComparer.Ordinal)
            {
                [sourceLocation.ShardId] = sourceShard,
                [targetLocation.ShardId] = targetShard,
            };
        }

        public Task<CpgShardLocation> WriteAsync(CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CpgFrozenShard> ReadAsync(CpgShardLocation location, CancellationToken cancellationToken) => Task.FromResult(_shards[location.ShardId]);
        public Task<CpgFrozenShard?> TryReadAsync(CpgShardLocation location, CpgShardLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(CpgFrozenShard Shard, CpgShardLocation Location)> ReadFromPathAsync(string shardPath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
