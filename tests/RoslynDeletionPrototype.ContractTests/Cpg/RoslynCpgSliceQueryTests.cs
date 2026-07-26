using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using MinimalRoslynCpg.Persistence;
using MinimalRoslynCpg.Persistence.Sqlite;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Analysis;
using NLISSN.Rules;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RoslynCpgSliceQueryTests
{
    [Fact]
    public async Task QueryBackwardAsync_BoundaryManifest_TraversesWithoutDuplicatedEndpointNodes()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpg-boundary-manifest-slice-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = new CpgFileKey("project", "input.cs", "source");
            var sourceLookup = new CpgShardLookup(file, new CpgFragmentKey("source", 0, 10, "source"), 1, "profile");
            var sinkLookup = new CpgShardLookup(file, new CpgFragmentKey("sink", 10, 10, "sink"), 1, "profile");
            var boundaryLookup = new CpgShardLookup(file, new CpgFragmentKey("boundary-adjacency", 10, 10, "incoming-boundary"), 1, "profile");
            var store = new CpgShardStore(root);
            var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
            await PublishBuildAsync(store, catalog,
                new CpgFrozenShard(
                    sourceLookup,
                    new[] { FrozenNode(0, 1, "source") },
                    Array.Empty<CpgFrozenEdge>(),
                    Array.Empty<CpgSymbolLocation>()),
                new CpgFrozenShard(
                    sinkLookup,
                    new[] { FrozenNode(0, 3, "sink") },
                    Array.Empty<CpgFrozenEdge>(),
                    Array.Empty<CpgSymbolLocation>()),
                new CpgFrozenShard(
                    boundaryLookup,
                    Array.Empty<CpgFrozenNode>(),
                    Array.Empty<CpgFrozenEdge>(),
                    Array.Empty<CpgSymbolLocation>(),
                    new[] { new CpgFrozenBoundaryEdge(1, 3, "DataFlow", null, null) },
                    CpgShardRole.BoundaryAdjacency,
                    new CpgBoundaryAdjacency(
                        CreateOwnerFragmentId(sinkLookup),
                        CpgBoundaryAdjacencyDirection.Incoming)));

            var query = new RoslynCpgSliceQuery(new CpgShardQueryResolver(catalog, store, maxCachedBytes: 1024 * 1024));
            var options = new RoslynCpgSliceQueryOptions(
                new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
                MaxHops: 1,
                MaxPaths: 1,
                MaxDefinitions: 1);

            var result = await query.QueryBackwardAsync(new NodeId(3), options, CancellationToken.None);

            Assert.Equal(new[] { new NodeId(1), new NodeId(3) }, Assert.Single(result.Paths).NodeIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QueryBackwardAsync_ShardResolver_LegacyV5AndV6Shards_ReturnSamePaths()
    {
        var legacyRoot = Path.Combine(Path.GetTempPath(), "cpg-shard-slice-legacy-v5-tests", Guid.NewGuid().ToString("N"));
        var currentRoot = Path.Combine(Path.GetTempPath(), "cpg-shard-slice-v6-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacyRoot);
        Directory.CreateDirectory(currentRoot);
        try
        {
            var file = new CpgFileKey("project", "input.cs", "source");
            var sourceLookup = new CpgShardLookup(file, new CpgFragmentKey("source", 0, 10, "source"), 1, "profile");
            var sinkLookup = new CpgShardLookup(file, new CpgFragmentKey("sink", 10, 10, "sink"), 1, "profile");
            var boundaryLookup = new CpgShardLookup(file, new CpgFragmentKey("boundary", 20, 10, "boundary"), 1, "profile");
            var sourceShard = new CpgFrozenShard(
                sourceLookup,
                new[] { FrozenNode(0, 1, "source") },
                Array.Empty<CpgFrozenEdge>(),
                Array.Empty<CpgSymbolLocation>());
            var sinkShard = new CpgFrozenShard(
                sinkLookup,
                new[]
                {
                    FrozenNode(0, 2, "middle"),
                    FrozenNode(1, 3, "sink"),
                },
                new[] { new CpgFrozenEdge(0, 1, "DataFlow", null, null) },
                Array.Empty<CpgSymbolLocation>());
            var boundaryShard = new CpgFrozenShard(
                boundaryLookup,
                Array.Empty<CpgFrozenNode>(),
                Array.Empty<CpgFrozenEdge>(),
                Array.Empty<CpgSymbolLocation>(),
                new[] { new CpgFrozenBoundaryEdge(1, 3, "DataFlow", null, null) },
                CpgShardRole.BoundaryAdjacency,
                new CpgBoundaryAdjacency(CreateOwnerFragmentId(sinkLookup), CpgBoundaryAdjacencyDirection.Incoming));
            var legacyStore = new CpgShardStore(legacyRoot);
            var legacyCatalog = new SqliteCpgShardCatalog(Path.Combine(legacyRoot, "catalog.db"));
            var currentStore = new CpgShardStore(currentRoot);
            var currentCatalog = new SqliteCpgShardCatalog(Path.Combine(currentRoot, "catalog.db"));
            await PublishLegacyV5BuildAsync(legacyRoot, legacyCatalog, sourceShard, sinkShard, boundaryShard);
            await PublishBuildAsync(currentStore, currentCatalog, sourceShard, sinkShard, boundaryShard);
            var options = new RoslynCpgSliceQueryOptions(
                new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
                MaxHops: 1,
                MaxPaths: 2,
                MaxDefinitions: 2);

            var legacy = await new RoslynCpgSliceQuery(
                new CpgShardQueryResolver(legacyCatalog, legacyStore, maxCachedBytes: 1024 * 1024))
                .QueryBackwardAsync(new NodeId(3), options, CancellationToken.None);
            var current = await new RoslynCpgSliceQuery(
                new CpgShardQueryResolver(currentCatalog, currentStore, maxCachedBytes: 1024 * 1024))
                .QueryBackwardAsync(new NodeId(3), options, CancellationToken.None);

            Assert.Equal(
                legacy.Paths.Select(path => string.Join("->", path.NodeIds)),
                current.Paths.Select(path => string.Join("->", path.NodeIds)));
        }
        finally
        {
            Directory.Delete(legacyRoot, recursive: true);
            Directory.Delete(currentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task QueryBackwardAsync_ShardResolver_TraversesAcrossAdjacentShards()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpg-shard-slice-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = new CpgFileKey("project", "input.cs", "source");
            var firstLookup = new CpgShardLookup(file, new CpgFragmentKey("first", 0, 10, "first"), 1, "profile");
            var secondLookup = new CpgShardLookup(file, new CpgFragmentKey("second", 10, 10, "second"), 1, "profile");
            var store = new CpgShardStore(root);
            var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
            await PublishShardAsync(store, catalog, new CpgFrozenShard(
                firstLookup,
                new[]
                {
                    FrozenNode(0, 1, "source"),
                    FrozenNode(1, 2, "middle"),
                },
                new[] { new CpgFrozenEdge(0, 1, "DataFlow", null, null) },
                Array.Empty<CpgSymbolLocation>()));
            await PublishShardAsync(store, catalog, new CpgFrozenShard(
                secondLookup,
                new[]
                {
                    FrozenNode(0, 2, "middle"),
                    FrozenNode(1, 3, "sink"),
                },
                new[] { new CpgFrozenEdge(0, 1, "DataFlow", null, null) },
                Array.Empty<CpgSymbolLocation>()));
            var query = new RoslynCpgSliceQuery(new CpgShardQueryResolver(catalog, store, maxCachedBytes: 1024 * 1024));
            var options = new RoslynCpgSliceQueryOptions(
                new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
                MaxHops: 2,
                MaxPaths: 4,
                MaxDefinitions: 4);

            var result = await query.QueryBackwardAsync(new NodeId(3), options, CancellationToken.None);

            Assert.Empty(result.UnavailableShards ?? Array.Empty<CpgShardUnavailableResult>());
            Assert.Equal(new[] { new NodeId(1), new NodeId(2), new NodeId(3) }, Assert.Single(result.Paths).NodeIds);
            Assert.True(result.ShardTelemetry!.OpenCount >= 2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QueryBackwardAsync_ShardResolver_ProjectsOnlyReachableShardRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpg-shard-slice-projection-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = new CpgFileKey("project", "input.cs", "source");
            var lookup = new CpgShardLookup(file, new CpgFragmentKey("method", 0, 10, "method"), 1, "profile");
            var store = new CpgShardStore(root);
            var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
            await PublishShardAsync(store, catalog, new CpgFrozenShard(
                lookup,
                new[]
                {
                    FrozenNode(0, 1, "source"),
                    FrozenNode(1, 2, "sink"),
                    new CpgFrozenNode(2, 99, "UnknownNodeKind", "input.cs", null, null, "Unknown", null, null, null, false),
                },
                new[] { new CpgFrozenEdge(0, 1, "DataFlow", null, null) },
                Array.Empty<CpgSymbolLocation>()));
            var query = new RoslynCpgSliceQuery(new CpgShardQueryResolver(catalog, store, maxCachedBytes: 1024 * 1024));
            var options = new RoslynCpgSliceQueryOptions(
                new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
                MaxHops: 1,
                MaxPaths: 1,
                MaxDefinitions: 1);

            var result = await query.QueryBackwardAsync(new NodeId(2), options, CancellationToken.None);

            Assert.Equal(new[] { new NodeId(1), new NodeId(2) }, Assert.Single(result.Paths).NodeIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QueryBackwardAsync_ShardResolver_RespectsVisitedNodeBudgetBeforeExpandingFrontier()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpg-shard-slice-node-budget-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = new CpgFileKey("project", "input.cs", "source");
            var lookup = new CpgShardLookup(file, new CpgFragmentKey("method", 0, 10, "method"), 1, "profile");
            var store = new CpgShardStore(root);
            var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
            await PublishShardAsync(store, catalog, new CpgFrozenShard(
                lookup,
                new[] { FrozenNode(0, 1, "source"), FrozenNode(1, 2, "sink") },
                new[] { new CpgFrozenEdge(0, 1, "DataFlow", null, null) },
                Array.Empty<CpgSymbolLocation>()));
            var query = new RoslynCpgSliceQuery(new CpgShardQueryResolver(catalog, store, maxCachedBytes: 1024 * 1024));
            var options = new RoslynCpgSliceQueryOptions(
                new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
                MaxHops: 4,
                MaxPaths: 4,
                MaxDefinitions: 4,
                MaxVisitedNodes: 1);

            var result = await query.QueryBackwardAsync(new NodeId(2), options, CancellationToken.None);

            Assert.Empty(result.Paths);
            Assert.True(result.WasTruncated);
            Assert.Equal("maxVisitedNodes", result.TruncationReason);
            Assert.Equal(1, result.VisitedNodeCount);
            Assert.Equal(1, result.ShardTelemetry!.LookupCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QueryBackwardAsync_ShardResolver_MissingAnchorReportsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpg-shard-slice-unavailable-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new CpgShardStore(root);
            var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
            var query = new RoslynCpgSliceQuery(new CpgShardQueryResolver(catalog, store, maxCachedBytes: 0));
            var options = new RoslynCpgSliceQueryOptions(
                new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
                MaxHops: 1,
                MaxPaths: 1,
                MaxDefinitions: 1);

            var result = await query.QueryBackwardAsync(new NodeId(99), options, CancellationToken.None);

            Assert.Empty(result.Paths);
            var unavailable = Assert.Single(result.UnavailableShards ?? Array.Empty<CpgShardUnavailableResult>());
            Assert.Equal(new NodeId(99), unavailable.NodeId);
            Assert.Equal("anchorUnavailable", unavailable.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FreezeQueryIndex_GraphSnapshotVersion_IsStableAndChangesWithGraphContent()
    {
        var firstGraph = new RoslynCpgGraph();
        var firstSource = CreateNode("source");
        var firstSink = CreateNode("sink");
        firstGraph.AddEdge(
            firstSource,
            firstSink,
            RoslynCpgEdgeKind.DataFlow,
            contextId: new RoslynCpgContextId("flow"));
        firstGraph.FreezeQueryIndex();

        var equivalentGraph = new RoslynCpgGraph();
        var equivalentSource = CreateNode("source");
        var equivalentSink = CreateNode("sink");
        equivalentGraph.AddEdge(
            equivalentSource,
            equivalentSink,
            RoslynCpgEdgeKind.DataFlow,
            contextId: new RoslynCpgContextId("flow"));
        equivalentGraph.FreezeQueryIndex();

        var changedGraph = new RoslynCpgGraph();
        var changedSource = CreateNode("source");
        var changedSink = CreateNode("sink");
        changedGraph.AddEdge(
            changedSource,
            changedSink,
            RoslynCpgEdgeKind.DataFlow,
            contextId: new RoslynCpgContextId("changed-flow"));
        changedGraph.FreezeQueryIndex();

        Assert.Equal(firstGraph.GraphSnapshotVersion, equivalentGraph.GraphSnapshotVersion);
        Assert.NotEqual(firstGraph.GraphSnapshotVersion, changedGraph.GraphSnapshotVersion);
        Assert.Equal(64, firstGraph.GraphSnapshotVersion.Length);
    }

    [Fact]
    public void QueryBackward_ReturnsStablePathsAndReportsHopBudgetExhaustion()
    {
        var graph = new RoslynCpgGraph();
        var source = CreateNode("source");
        var middle = CreateNode("middle");
        var sink = CreateNode("sink");
        graph.AddEdge(source, middle, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(middle, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 1,
            MaxPaths: 10,
            MaxDefinitions: 10);

        var frozenMiddle = FreezeLookup(graph, "middle");
        var frozenSink = FreezeLookup(graph, "sink");
        var result = query.QueryBackward(frozenSink.NodeId!.Value, options);

        var path = Assert.Single(result.Paths);
        Assert.Equal(frozenMiddle.NodeId!.Value, path.SourceNodeId);
        Assert.Equal(frozenSink.NodeId!.Value, path.SinkNodeId);
        Assert.Equal(new[] { frozenMiddle.NodeId.Value, frozenSink.NodeId.Value }, path.NodeIds);
        Assert.True(result.WasTruncated);
        Assert.Equal("maxHops", result.TruncationReason);
    }

    [Fact]
    public void MarkAnalysisSnapshot_ReusesSliceResultForSameQueryKey()
    {
        const string source = "public sealed class Sample { public int Run(int value) { return value; } }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "slice-cache.cs");
        var compilation = CSharpCompilation.Create(
            "SliceCacheTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, "slice-cache.cs");
        var analysisContext = new CpgAnalysisContext(
            graph,
            compilation.GetSemanticModel(tree),
            tree.GetRoot());
        var snapshot = new MarkAnalysisSnapshot(analysisContext);
        var ruleContext = new RuleContext(
            analysisContext,
            new Dictionary<string, string>(),
            markAnalysisSnapshot: snapshot);
        var sinkNode = graph.Nodes.First(node => node.Kind == RoslynCpgNodeKind.MethodReturn);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 3,
            MaxPaths: 10,
            MaxDefinitions: 10);

        var sinkNodeId = sinkNode.NodeId!.Value;
        var first = ruleContext.QuerySliceBackward(sinkNodeId, options);
        var second = ruleContext.QuerySliceBackward(sinkNodeId, options);

        Assert.Same(first, second);
        Assert.Equal(1, snapshot.Telemetry.SliceQueryCacheMissCount);
        Assert.Equal(1, snapshot.Telemetry.SliceQueryCacheHitCount);
        Assert.Equal(
            graph.Edges
                .Where(edge => edge.SourceNodeId == sinkNodeId && edge.Kind == RoslynCpgEdgeKind.DataFlow)
                .OrderBy(edge => edge.TargetNodeId),
            ruleContext.GetGraphEdgesByKind(sinkNodeId, RoslynCpgEdgeKind.DataFlow)
                .OrderBy(edge => edge.TargetNodeId));
    }

    [Theory]
    [InlineData(1, 10, "maxPaths")]
    [InlineData(10, 1, "maxDefinitions")]
    public void QueryBackward_WhenResultBudgetIsReached_ReportsTheExhaustedBudget(int maxPaths, int maxDefinitions, string expectedReason)
    {
        var graph = new RoslynCpgGraph();
        var firstSource = CreateNode("first-source");
        var secondSource = CreateNode("second-source");
        var sink = CreateNode("sink");
        graph.AddEdge(firstSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(secondSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 2,
            MaxPaths: maxPaths,
            MaxDefinitions: maxDefinitions);

        var frozenSink = FreezeLookup(graph, "sink");
        var result = query.QueryBackward(frozenSink.NodeId!.Value, options);

        Assert.True(result.WasTruncated);
        Assert.Equal(expectedReason, result.TruncationReason);
        Assert.True(result.Paths.Count <= maxPaths);
        Assert.True(result.Paths.Count <= maxDefinitions);
        Assert.Equal(
            result.Paths.OrderBy(path => path.SourceNodeId),
            result.Paths);
    }

    [Fact]
    public void QueryBackward_WhenTraversalContainsCycle_TerminatesWithStableEmptyResult()
    {
        var graph = new RoslynCpgGraph();
        var first = CreateNode("first");
        var second = CreateNode("second");
        var sink = CreateNode("sink");
        graph.AddEdge(first, second, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(second, first, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(second, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 10,
            MaxPaths: 10,
            MaxDefinitions: 10);

        var result = query.QueryBackward(FreezeLookup(graph, "sink").NodeId!.Value, options);

        Assert.Empty(result.Paths);
        Assert.False(result.WasTruncated);
        Assert.InRange(result.VisitedNodeCount, 1, 3);
    }

    [Fact]
    public void QueryBackward_WhenVisitedNodeBudgetIsReached_ReportsMaxVisitedNodes()
    {
        var graph = new RoslynCpgGraph();
        var source = CreateNode("source");
        var middle = CreateNode("middle");
        var sink = CreateNode("sink");
        graph.AddEdge(source, middle, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(middle, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 4,
            MaxPaths: 4,
            MaxDefinitions: 4,
            MaxVisitedNodes: 1);

        var result = query.QueryBackward(FreezeLookup(graph, "sink").NodeId!.Value, options);

        Assert.True(result.WasTruncated);
        Assert.Equal("maxVisitedNodes", result.TruncationReason);
        Assert.Equal(1, result.VisitedNodeCount);
    }

    [Fact]
    public void QueryBackward_RepeatedEquivalentQuery_UsesCache()
    {
        var graph = new RoslynCpgGraph();
        var firstSource = CreateNode("first-source");
        var secondSource = CreateNode("second-source");
        var sink = CreateNode("sink");
        graph.AddEdge(firstSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(secondSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 2,
            MaxPaths: 4,
            MaxDefinitions: 4,
            MaxVisitedEdges: 2);

        var sinkNodeId = FreezeLookup(graph, "sink").NodeId!.Value;
        var first = query.QueryBackward(sinkNodeId, options);
        var second = query.QueryBackward(sinkNodeId, options);

        Assert.False(first.WasTruncated);
        Assert.Equal(1, first.Telemetry.CacheMissCount);
        Assert.Equal(1, second.Telemetry.CacheHitCount);
        Assert.Equal(first.Paths, second.Paths);
    }

    [Fact]
    public void QueryBackward_WhenVisitedEdgeBudgetIsReached_ReportsMaxVisitedEdges()
    {
        var graph = new RoslynCpgGraph();
        var firstSource = CreateNode("first-source");
        var secondSource = CreateNode("second-source");
        var sink = CreateNode("sink");
        graph.AddEdge(firstSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(secondSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 2,
            MaxPaths: 4,
            MaxDefinitions: 4,
            MaxVisitedEdges: 1);

        var result = query.QueryBackward(FreezeLookup(graph, "sink").NodeId!.Value, options);

        Assert.True(result.WasTruncated);
        Assert.Equal("maxVisitedEdges", result.TruncationReason);
        Assert.Equal(1, result.VisitedEdgeCount);
    }

    [Fact]
    public void QueryBackward_InterproceduralEdgesConsumeOnlyCallDepthBudget()
    {
        var graph = new RoslynCpgGraph();
        var source = CreateNode("source");
        var parameter = CreateNode("parameter");
        var sink = CreateNode("sink");
        graph.AddEdge(source, parameter, RoslynCpgEdgeKind.InterproceduralDataFlow);
        graph.AddEdge(parameter, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var edgeKinds = new HashSet<RoslynCpgEdgeKind>
        {
            RoslynCpgEdgeKind.DataFlow,
            RoslynCpgEdgeKind.InterproceduralDataFlow,
        };

        var frozenSource = FreezeLookup(graph, "source");
        var frozenParameter = FreezeLookup(graph, "parameter");
        var frozenSink = FreezeLookup(graph, "sink");
        var blocked = query.QueryBackward(frozenSink.NodeId!.Value, new RoslynCpgSliceQueryOptions(edgeKinds, 4, 4, 4));
        var allowed = query.QueryBackward(frozenSink.NodeId.Value, new RoslynCpgSliceQueryOptions(edgeKinds, 4, 4, 4, MaxCallDepth: 1));

        Assert.True(blocked.WasTruncated);
        Assert.Equal("maxCallDepth", blocked.TruncationReason);
        Assert.Equal(
            new[] { frozenSource.NodeId!.Value, frozenParameter.NodeId!.Value, frozenSink.NodeId!.Value },
            Assert.Single(allowed.Paths).NodeIds);
    }

    [Fact]
    public void QueryBackward_WhenInterproceduralFrameRepeats_CutsTheRecursiveCallStack()
    {
        var graph = new RoslynCpgGraph();
        var source = CreateNode("source");
        var recursiveParameter = CreateNode("recursive-parameter");
        var sink = CreateNode("sink");
        graph.AddEdge(
          source,
          recursiveParameter,
          RoslynCpgEdgeKind.InterproceduralDataFlow,
          RoslynCpgEdgeLabel.ForInterproceduralBridge(
            RoslynCpgInterproceduralBridgeKind.ArgumentToParameter),
          callSiteContext: new RoslynCpgCallSiteContext(
            "recursive.cs",
            10,
            20,
            "Recursive"));
        graph.AddEdge(
          recursiveParameter,
          sink,
          RoslynCpgEdgeKind.InterproceduralDataFlow,
          RoslynCpgEdgeLabel.ForInterproceduralBridge(
            RoslynCpgInterproceduralBridgeKind.MethodReturnToCallResult),
          callSiteContext: new RoslynCpgCallSiteContext(
            "recursive.cs",
            10,
            20,
            "Recursive"));
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.InterproceduralDataFlow },
            MaxHops: 4,
            MaxPaths: 4,
            MaxDefinitions: 4,
            MaxCallDepth: 4);

        var result = query.QueryBackward(FreezeLookup(graph, "sink").NodeId!.Value, options);

        Assert.True(result.WasTruncated);
        Assert.Equal("callStackCycle", result.TruncationReason);
        Assert.True(result.Telemetry!.MaxObservedCallDepth >= 1);
    }

    [Fact]
    public void QueryBackward_RepeatedReferenceFlow_KeepsUniqueDeterministicPaths()
    {
        const string source = """
            namespace Demo;

            public sealed class SliceDuplicateSample
            {
                public int Run(int seed)
                {
                    var value = seed + 1;
                    return value + value + value;
                }
            }
            """;
        var firstGraph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, "slice-duplicate-a.cs");
        var secondGraph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, "slice-duplicate-b.cs");
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 4,
            MaxPaths: 10,
            MaxDefinitions: 10);

        var firstResult = QueryMethodReturnBackward(firstGraph, options);
        var secondResult = QueryMethodReturnBackward(secondGraph, options);
        _ = Assert.Single(firstResult.Paths);

        Assert.False(firstResult.WasTruncated);
        Assert.Equal(firstResult.Paths.Count, firstResult.Paths.Distinct().Count());
        Assert.Equal(FormatPaths(firstResult.Paths), FormatPaths(secondResult.Paths));
    }

    [Fact]
    public async Task ShardResolver_CachePromotesHitsAndEvictsLeastRecentlyUsedShard()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpg-shard-resolver-lru-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = new CpgFileKey("project", "input.cs", "source");
            var store = new CpgShardStore(root);
            var catalog = new SqliteCpgShardCatalog(Path.Combine(root, "catalog.db"));
            var first = await WriteAndPublishAsync(
                store,
                catalog,
                new CpgFrozenShard(
                    new CpgShardLookup(file, new CpgFragmentKey("one", 0, 10, "one"), 1, "profile"),
                    new[] { FrozenNode(0, 1, "one") },
                    Array.Empty<CpgFrozenEdge>(),
                    Array.Empty<CpgSymbolLocation>()));
            var second = await WriteAndPublishAsync(
                store,
                catalog,
                new CpgFrozenShard(
                    new CpgShardLookup(file, new CpgFragmentKey("two", 10, 10, "two"), 1, "profile"),
                    new[] { FrozenNode(0, 2, "two") },
                    Array.Empty<CpgFrozenEdge>(),
                    Array.Empty<CpgSymbolLocation>()));
            var third = await WriteAndPublishAsync(
                store,
                catalog,
                new CpgFrozenShard(
                    new CpgShardLookup(file, new CpgFragmentKey("tri", 20, 10, "tri"), 1, "profile"),
                    new[] { FrozenNode(0, 3, "tri") },
                    Array.Empty<CpgFrozenEdge>(),
                    Array.Empty<CpgSymbolLocation>()));
            var resolver = new CpgShardQueryResolver(
                catalog,
                store,
                maxCachedBytes: first.ByteLength + second.ByteLength);

            _ = Assert.Single(await resolver.FindByNodeAsync(new NodeId(1), CancellationToken.None));
            _ = Assert.Single(await resolver.FindByNodeAsync(new NodeId(2), CancellationToken.None));
            _ = Assert.Single(await resolver.FindByNodeAsync(new NodeId(1), CancellationToken.None));
            _ = Assert.Single(await resolver.FindByNodeAsync(new NodeId(3), CancellationToken.None));
            _ = Assert.Single(await resolver.FindByNodeAsync(new NodeId(2), CancellationToken.None));

            var telemetry = resolver.GetTelemetry();
            Assert.Equal(5, telemetry.LookupCount);
            Assert.Equal(4, telemetry.OpenCount);
            Assert.Equal(1, telemetry.CacheHitCount);
            Assert.Equal(4, telemetry.CacheMissCount);
            Assert.Equal(2, telemetry.EvictionCount);
            Assert.Equal(
                first.ByteLength + second.ByteLength + third.ByteLength + second.ByteLength,
                telemetry.BytesRead);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RoslynCpgNode CreateNode(string id)
    {
        return new RoslynCpgNode(RoslynCpgNodeKind.Operation, "Operation", Name: id);
    }

    private static RoslynCpgNode FreezeLookup(RoslynCpgGraph graph, string displayId)
    {
        return Assert.Single(graph.Nodes, node => node.Name == displayId);
    }

    private static RoslynCpgSliceResult QueryMethodReturnBackward(RoslynCpgGraph graph, RoslynCpgSliceQueryOptions options)
    {
        graph.FreezeQueryIndex();
        var methodReturn = Assert.Single(graph.Nodes, node => node.Kind == RoslynCpgNodeKind.MethodReturn);
        return new RoslynCpgSliceQuery(graph).QueryBackward(methodReturn.NodeId!.Value, options);
    }

    private static string[] FormatPaths(IReadOnlyList<RoslynCpgSlicePath> paths)
    {
        return paths
            .Select(path => string.Join("->", path.NodeIds))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();
    }

    private static CpgFrozenNode FrozenNode(int localIndex, uint nodeId, string name)
    {
        return new CpgFrozenNode(localIndex, nodeId, "Operation", "input.cs", null, null, "Operation", name, null, null, false);
    }

    private static async Task PublishBuildAsync(CpgShardStore store, SqliteCpgShardCatalog catalog, params CpgFrozenShard[] shards)
    {
        var buildId = await catalog.BeginBuildAsync(CancellationToken.None);
        foreach (var shard in shards)
        {
            var result = await store.WriteAsync(shard, CancellationToken.None);
            await catalog.StageAsync(
                buildId,
                new CpgShardLease(shard.Lookup, result.Location),
                shard,
                CancellationToken.None);
        }

        await catalog.CompleteBuildAsync(buildId, CancellationToken.None);
    }

    private static async Task PublishShardAsync(CpgShardStore store, SqliteCpgShardCatalog catalog, CpgFrozenShard shard)
    {
        var result = await store.WriteAsync(shard, CancellationToken.None);
        await catalog.PublishAsync(
            new CpgShardLease(shard.Lookup, result.Location),
            shard,
            CancellationToken.None);
    }

    private static async Task<CpgShardLocation> WriteAndPublishAsync(CpgShardStore store, SqliteCpgShardCatalog catalog, CpgFrozenShard shard)
    {
        var result = await store.WriteAsync(shard, CancellationToken.None);
        await catalog.PublishAsync(
            new CpgShardLease(shard.Lookup, result.Location),
            shard,
            CancellationToken.None);
        return result.Location;
    }

    private static async Task PublishLegacyV5BuildAsync(string root, SqliteCpgShardCatalog catalog, params CpgFrozenShard[] shards)
    {
        var buildId = await catalog.BeginBuildAsync(CancellationToken.None);
        foreach (var shard in shards)
        {
            var location = await WriteLegacyV5ShardAsync(root, shard);
            await catalog.StageAsync(
                buildId,
                new CpgShardLease(shard.Lookup, location),
                shard,
                CancellationToken.None);
        }

        await catalog.CompleteBuildAsync(buildId, CancellationToken.None);
    }

    private static string CreateOwnerFragmentId(CpgShardLookup lookup)
    {
        return string.Join("|", lookup.File.ProjectId, lookup.File.RelativePath,
            lookup.File.SourceHash, lookup.Fragment.Kind, lookup.Fragment.SpanStart,
            lookup.Fragment.SpanLength, lookup.Fragment.FragmentHash,
            lookup.SchemaVersion, lookup.ProfileHash);
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
        WriteRequired(writer, shard.Lookup.File.ProjectId);
        WriteRequired(writer, shard.Lookup.File.RelativePath);
        WriteRequired(writer, shard.Lookup.File.SourceHash);
        WriteRequired(writer, shard.Lookup.Fragment.Kind);
        writer.Write(shard.Lookup.Fragment.SpanStart);
        writer.Write(shard.Lookup.Fragment.SpanLength);
        WriteRequired(writer, shard.Lookup.Fragment.FragmentHash);
        writer.Write(shard.Lookup.SchemaVersion);
        WriteRequired(writer, shard.Lookup.ProfileHash);
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
}
