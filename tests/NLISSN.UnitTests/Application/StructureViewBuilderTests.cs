using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Analysis;
using NLISSN.Core.Analysis;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using RoslynPrototype.Tests.TestCodeSet.Target;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class StructureViewBuilderTests
{
    [Fact]
    public void QueryIndex_AfterFreeze_ProvidesStableSortedAdjacencyAndEdgesByKind()
    {
        var graph = new NLCPG.Model.NLCPGGraph();
        var first = CreateLabeledNode("first");
        var second = CreateLabeledNode("second");
        var third = CreateLabeledNode("third");

        graph.AddEdge(first, third, NLCPGEdgeKind.DataFlow);
        graph.AddEdge(first, second, NLCPGEdgeKind.OpChild);

        Assert.False(graph.HasQueryIndex);
        graph.FreezeQueryIndex();
        var frozenFirst = FindNode(graph, "first");
        var frozenSecond = FindNode(graph, "second");
        var frozenThird = FindNode(graph, "third");

        Assert.True(graph.HasQueryIndex);
        Assert.Equal(new[] { RequireNodeId(frozenSecond), RequireNodeId(frozenThird) }, graph.GetOutgoingEdges(RequireNodeId(frozenFirst)).Select(edge => edge.TargetNodeId));
        Assert.Equal(new[] { RequireNodeId(frozenFirst) }, graph.GetIncomingEdges(RequireNodeId(frozenThird)).Select(edge => edge.SourceNodeId));
        Assert.Equal(new[] { RequireNodeId(frozenFirst) }, graph.GetEdges(NLCPGEdgeKind.DataFlow).Select(edge => edge.SourceNodeId));
        Assert.Throws<InvalidOperationException>(() => graph.AddNode(CreateLabeledNode("fourth")));
    }

    [Fact]
    public void QueryIndex_AfterFreeze_ProvidesStableNodesByKind()
    {
        var graph = new NLCPG.Model.NLCPGGraph();
        var second = CreateLabeledNode("second");
        var first = CreateLabeledNode("first");
        var method = new NLCPG.Model.NLCPGNode(NLCPGNodeKind.Method, "Method", Name: "method");

        graph.AddNode(second);
        graph.AddNode(method);
        graph.AddNode(first);
        graph.FreezeQueryIndex();

        Assert.Equal(
            graph.GetNodes(NLCPGNodeKind.Operation).OrderBy(node => node.NodeId).Select(node => node.Name),
            graph.GetNodes(NLCPGNodeKind.Operation).Select(node => node.Name));
    }

    [Fact]
    public void GetGraphNodesByKind_AfterFreeze_MatchesGraphNodeIndexOrder()
    {
        var (context, _) = CreateAnalysisContext(
            "public sealed class Sample { public int Run(int value) => value + 1; }",
            "rule-context-nodes-by-kind.cs");
        var session = new AnalysisSession(
            context,
            AnalysisLegacyOptionsTestExtensions.CreateSettings(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

        var nodes = session.GetGraphNodesByKind(NLCPGNodeKind.Operation);

        Assert.Equal(
            context.Graph.GetNodes(NLCPGNodeKind.Operation).Select(node => node.NodeId),
            nodes.Select(node => node.NodeId));
    }

    [Fact]
    public void QueryIndex_AfterFreeze_ResolvesSymbolReferencesCallsitesAndHalfOpenFileSpan()
    {
        var graph = new NLCPG.Model.NLCPGGraph();
        var symbol = new NLCPG.Model.NLCPGNode(NLCPGNodeKind.SymbolMethod, "SymbolMethod", Name: "symbol", FullName: "Demo.Callee");
        var reference = new NLCPG.Model.NLCPGNode(NLCPGNodeKind.Reference, "Reference", Name: "reference", FilePath: "sample.cs", SpanStart: 2, SpanEnd: 4);
        var callSite = new NLCPG.Model.NLCPGNode(NLCPGNodeKind.CallSite, "CallSite", Name: "call", FilePath: "sample.cs", SpanStart: 4, SpanEnd: 8);
        var method = new NLCPG.Model.NLCPGNode(NLCPGNodeKind.Method, "Method", Name: "method", FullName: "Demo.Caller");
        graph.AddEdge(reference, symbol, NLCPGEdgeKind.Ref);
        graph.AddEdge(callSite, symbol, NLCPGEdgeKind.CallTargets);
        graph.AddEdge(method, callSite, NLCPGEdgeKind.ContainsSymbol);
        graph.FreezeQueryIndex();
        var frozenSymbol = FindNode(graph, "symbol");
        var frozenMethod = FindNode(graph, "method");

        Assert.Equal(new[] { "reference" }, graph.GetSymbolReferences(RequireNodeId(frozenSymbol)).Select(node => node.Name));
        Assert.Equal(new[] { "call" }, graph.GetMethodOwnedCallSites(RequireNodeId(frozenMethod)).Select(node => node.Name));
        Assert.Equal(new[] { "reference" }, graph.GetNodesInFileSpan("sample.cs", 2, 4).Select(node => node.Name));
        Assert.NotEqual(0, graph.GetEdgeMaskId(new HashSet<NLCPGEdgeKind> { NLCPGEdgeKind.DataFlow }));
    }

    [Fact]
    public void ExtractLocalView_AfterFreeze_AppliesDirectionKindAndHopLimits()
    {
        var graph = new NLCPG.Model.NLCPGGraph();
        var first = CreateLabeledNode("first");
        var second = CreateLabeledNode("second");
        var third = CreateLabeledNode("third");
        var fourth = CreateLabeledNode("fourth");

        graph.AddEdge(first, second, NLCPGEdgeKind.DataFlow);
        graph.AddEdge(second, third, NLCPGEdgeKind.DataFlow);
        graph.AddEdge(second, fourth, NLCPGEdgeKind.OpChild);

        Assert.Throws<InvalidOperationException>(() => graph.ExtractLocalView(new NodeId(2), 1));
        graph.FreezeQueryIndex();
        var frozenFirst = FindNode(graph, "first");
        var frozenSecond = FindNode(graph, "second");

        var view = graph.ExtractLocalView(
            RequireNodeId(frozenSecond),
            1,
            NLCPGViewDirection.Incoming,
            new[] { NLCPGEdgeKind.DataFlow });

        Assert.Equal(new[] { RequireNodeId(frozenFirst), RequireNodeId(frozenSecond) }, view.Nodes.Select(node => node.NodeId!.Value));
        Assert.Equal(new[] { RequireNodeId(frozenFirst) }, view.Edges.Select(edge => edge.SourceNodeId));
        Assert.Equal(new[] { RequireNodeId(frozenSecond) }, view.Edges.Select(edge => edge.TargetNodeId));
    }

    [Fact]
    public void Build_ForSingleFragment_CopiesMainGraphNodesAndEdgesInsideFragment()
    {
        var source = AtomicExpressionSources.ReturnExpressionSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-single-fragment.cs");
        var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single();
        var expectedNodeIds = ResolveGraphNodeIdsInside(context, memberAccess);
        var expectedEdgeKeys = ResolveGraphEdgeKeysInside(context, expectedNodeIds)
            .Where(key => key.Contains($"|{NLCPGEdgeKind.SyntaxChild}|", StringComparison.Ordinal) ||
                key.Contains($"|{NLCPGEdgeKind.TokenChild}|", StringComparison.Ordinal) ||
                key.Contains($"|{NLCPGEdgeKind.SyntaxHasOperation}|", StringComparison.Ordinal) ||
                key.Contains($"|{NLCPGEdgeKind.OpHasSyntax}|", StringComparison.Ordinal))
            .ToArray();

        var view = new NLCPGStructureViewBuilder().Build(memberAccess, context);

        Assert.NotEmpty(expectedNodeIds);
        Assert.Equal(expectedNodeIds, view.Nodes.Select(node => node.NodeId!.Value).ToHashSet());
        Assert.Equal(expectedEdgeKeys, ResolveViewEdgeKeys(view));
        Assert.All(view.Edges, edge => Assert.Contains(edge, context.Graph.Edges));
    }

    [Fact]
    public void Build_ForMultipleFragments_CopiesMainGraphNodesAndConnectsFragments()
    {
        var source = AtomicExpressionSources.AtomicNameSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-multiple-fragments.cs");
        var declarator = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.ToString() == "s.Seed");
        var declaratorNodeIds = ResolveGraphNodeIdsInside(context, declarator);
        var memberAccessNodeIds = ResolveGraphNodeIdsInside(context, memberAccess);

        var view = BuildStructuralView(new SyntaxNode[] { declarator, memberAccess }, context);

        Assert.NotEmpty(declaratorNodeIds);
        Assert.NotEmpty(memberAccessNodeIds);
        var viewNodeIds = view.Nodes.Select(node => node.NodeId!.Value).ToHashSet();
        Assert.True(declaratorNodeIds.IsSubsetOf(viewNodeIds));
        Assert.True(memberAccessNodeIds.IsSubsetOf(viewNodeIds));
        Assert.True(HasUndirectedPath(view, declaratorNodeIds, memberAccessNodeIds));
        Assert.All(view.Edges, edge => Assert.Contains(edge, context.Graph.Edges));
    }

    [Fact]
    public void Build_ForMultipleFragments_AddsShortestIntermediateNodesOnly()
    {
        var source = """
            public sealed class Sample
            {
                public void Run()
                {
                    var first = 1;
                    var second = first + 2;
                }
            }
            """;
        var (context, root) = CreateAnalysisContext(source, "structure-view-shortest-path.cs");
        var literals = root.DescendantNodes().OfType<LiteralExpressionSyntax>().ToArray();
        var firstLiteral = literals.Single(node => node.Token.ValueText == "1");
        var secondLiteral = literals.Single(node => node.Token.ValueText == "2");
        var firstNodeIds = ResolveGraphNodeIdsInside(context, firstLiteral);
        var secondNodeIds = ResolveGraphNodeIdsInside(context, secondLiteral);

        var view = BuildStructuralView(new SyntaxNode[] { firstLiteral, secondLiteral }, context);

        Assert.True(HasUndirectedPath(view, firstNodeIds, secondNodeIds));
        Assert.True(view.Nodes.Count > firstNodeIds.Union(secondNodeIds).Count());
        Assert.All(view.Edges, edge => Assert.Contains(edge, context.Graph.Edges));
    }

    [Fact]
    public void Build_ForSameFragments_ReusesCachedViewWithinAnalysisContext()
    {
        var source = AtomicExpressionSources.AtomicNameSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-cache.cs");
        var declarator = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.ToString() == "s.Seed");
        var builder = new NLCPGStructureViewBuilder();

        var firstView = BuildStructuralView(new SyntaxNode[] { declarator, memberAccess }, context);
        var secondView = BuildStructuralView(new SyntaxNode[] { declarator, memberAccess }, context);

        Assert.Same(firstView, secondView);
    }

    [Fact]
    public void Build_AfterRuntimeCacheInvalidation_DoesNotReuseCachedView()
    {
        var source = AtomicExpressionSources.AtomicNameSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-cache-epoch.cs");
        var declarator = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.ToString() == "s.Seed");
        var runtime =  AnalysisRuntime.CreateDefault();
        var firstSession = new AnalysisSession(
            context,
            AnalysisLegacyOptionsTestExtensions.CreateSettings(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            runtime: runtime);
        var secondSession = new AnalysisSession(
            context,
            AnalysisLegacyOptionsTestExtensions.CreateSettings(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            runtime: runtime.InvalidateCaches());

        var firstView = firstSession.BuildStructureView(
            new SyntaxNode[] { declarator, memberAccess },
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Bidirectional,
            new NLCPGTraversalBudget(16, 1, 1, 4096, 8192));
        var secondView = secondSession.BuildStructureView(
            new SyntaxNode[] { declarator, memberAccess },
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Bidirectional,
            new NLCPGTraversalBudget(16, 1, 1, 4096, 8192));

        Assert.NotSame(firstView, secondView);
    }

    [Fact]
    public void Build_ForSameFragmentsDifferentOrder_UsesRequestedFirstFragmentAsRoot()
    {
        var source = AtomicExpressionSources.AtomicNameSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-cache-order.cs");
        var declarator = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.ToString() == "s.Seed");
        var builder = new NLCPGStructureViewBuilder();

        var declaratorFirstView = BuildStructuralView(new SyntaxNode[] { declarator, memberAccess }, context);
        var memberAccessFirstView = BuildStructuralView(new SyntaxNode[] { memberAccess, declarator }, context);

        Assert.NotSame(declaratorFirstView, memberAccessFirstView);
        Assert.Equal(declarator.SpanStart, declaratorFirstView.Root.SpanStart);
        Assert.Equal(memberAccess.SpanStart, memberAccessFirstView.Root.SpanStart);
    }

    [Fact]
    public void Build_ForRepeatedRequests_ReusesCachedViewForTheSameFragmentOrder()
    {
        var source = AtomicExpressionSources.AtomicNameSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-cache-telemetry.cs");
        var declarator = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.ToString() == "s.Seed");
        var builder = new NLCPGStructureViewBuilder();

        var first = BuildStructuralView(new SyntaxNode[] { declarator, memberAccess }, context);
        var second = BuildStructuralView(new SyntaxNode[] { declarator, memberAccess }, context);
        var reversed = BuildStructuralView(new SyntaxNode[] { memberAccess, declarator }, context);

        Assert.Same(first, second);
        Assert.NotSame(first, reversed);
    }

    [Fact]
    public void Build_WhenSyntaxTreeHasNoPath_StillResolvesGraphNodes()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(Box s)
                {
                    return s.Seed;
                }
            }

            public sealed class Box
            {
                public int Seed { get; set; }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var compilation = CSharpCompilation.Create(
            "StructureViewBuilderNoPathTests",
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            });
        var semanticModel = compilation.GetSemanticModel(tree);
        var graphWithPath = new NLCPGBuilder().BuildFromSource(source, "structure-view-no-path.cs");
        var graph = new NLCPG.Model.NLCPGGraph();
        foreach (var node in graphWithPath.Nodes)
        {
            graph.AddNode(node with { FilePath = string.Empty });
        }

        var addedNodes = graphWithPath.Nodes
            .Select(node => graph.AddNode(node with { FilePath = string.Empty }))
            .ToDictionary(BuildNodeContractKey, StringComparer.Ordinal);

        foreach (var edge in graphWithPath.Edges)
        {
            var sourceNode = addedNodes[BuildNodeContractKey(graphWithPath.GetNode(edge.SourceNodeId)! with { FilePath = string.Empty })];
            var targetNode = addedNodes[BuildNodeContractKey(graphWithPath.GetNode(edge.TargetNodeId)! with { FilePath = string.Empty })];
            graph.AddEdge(
              sourceNode,
              targetNode,
              edge.Kind,
              edge.StructuredLabel,
              edge.ContextId);
        }

        var context = new CpgAnalysisContext(graph, semanticModel, root);
        var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single();

        var view = new NLCPGStructureViewBuilder().Build(memberAccess, context);

        Assert.NotEmpty(view.Nodes);
        Assert.Contains(view.Nodes, node => node.SpanStart == memberAccess.SpanStart);
    }

    [Fact]
    public void Query_WhenAnchorHasMultipleSyntaxRoots_ReturnsAmbiguous()
    {
        // Arrange
        const string source = "class Sample { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "structure-view-ambiguous.cs");
        var root = tree.GetRoot();
        var compilation = CSharpCompilation.Create(
            "StructureViewAmbiguousTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var graph = new NLCPGGraph();
        graph.AddNode(new NLCPGNode(
            NLCPGNodeKind.SyntaxNode,
            "SyntaxNode",
            "first",
            FilePath: tree.FilePath,
            SpanStart: root.SpanStart,
            SpanEnd: root.Span.End,
            StableAnchor: new StableNodeAnchor(
                NLCPGNodeKind.SyntaxNode, 1, root.SpanStart, root.Span.End, StableNodeRole.SyntaxNode, 0, 1)));
        graph.AddNode(new NLCPGNode(
            NLCPGNodeKind.SyntaxNode,
            "SyntaxNode",
            "second",
            FilePath: tree.FilePath,
            SpanStart: root.SpanStart,
            SpanEnd: root.Span.End,
            StableAnchor: new StableNodeAnchor(
                NLCPGNodeKind.SyntaxNode, 1, root.SpanStart, root.Span.End, StableNodeRole.SyntaxNode, 1, 2)));
        graph.FreezeQueryIndex();
        var context = new CpgAnalysisContext(graph, compilation.GetSemanticModel(tree), root);

        // Act
        var result = new NLCPGStructureViewBuilder().Query(
            new[] { root },
            context,
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Incoming,
            new NLCPGTraversalBudget(1, 1, 1, 16, 16));

        // Assert
        Assert.Equal(CpgQueryStatus.Ambiguous, result.Status);
        Assert.Null(result.View);
    }

    [Fact]
    public void AnalyzeBinaryExpression_ReturnsAffectedSyntaxTreeOnly()
    {
        var source = AtomicLogicalSources.LogicalAndConditionSource;
        var (context, root) = CreateAnalysisContext(source, "binary-structure-members.cs");
        var binaryExpression = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Single(node => node.IsKind(SyntaxKind.LogicalAndExpression));

        var analysis = new BinaryExpressionAnalyzer().Analyze(
            binaryExpression,
            binaryExpression.Right,
            context);

        Assert.Contains(binaryExpression, analysis.AffectedSyntaxTree);
        Assert.Contains(analysis.AffectedSyntaxTree, node => node.ToString() == "ready");
        Assert.Contains(analysis.AffectedSyntaxTree, node => node.ToString() == "s.IsReady");
    }

    [Fact]
    public void AnalyzeMarkRegion_ForMemberAccess_UsesContainingStatementAsRegion()
    {
        var source = AtomicExpressionSources.AtomicNameSource;
        var (context, root) = CreateAnalysisContext(source, "mark-region-statement.cs");
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.ToString() == "s.Seed");

        var region = new MarkRegionAnalyzer().Analyze(memberAccess, context);

        Assert.Equal(SyntaxKind.LocalDeclarationStatement, (SyntaxKind)region.RegionNode.RawKind);
        Assert.Contains("var value = s.Seed + offset;", region.RegionNode.ToString(), StringComparison.Ordinal);
        Assert.True(region.Span.Contains(memberAccess.Span));
        Assert.True(region.NodeCount > 1);
        Assert.True(region.ExpressionCount >= 2);
        Assert.Equal(1, region.StatementCount);
    }

    [Fact]
    public void AnalyzePropagationRegion_ForConditionMemberAccess_UsesContainingControlStatement()
    {
        var source = AtomicLogicalSources.LogicalAndConditionSource;
        var (context, root) = CreateAnalysisContext(source, "propagation-region-if.cs");
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.ToString() == "s.IsReady");

        var region = new PropagationRegionAnalyzer().Analyze(memberAccess, context);

        Assert.Equal(SyntaxKind.IfStatement, (SyntaxKind)region.RegionNode.RawKind);
        Assert.Contains("ready && s.IsReady", region.RegionNode.ToString(), StringComparison.Ordinal);
        Assert.True(region.Span.Contains(memberAccess.Span));
        Assert.True(region.NodeCount > 1);
        Assert.True(region.ExpressionCount >= 2);
        Assert.True(region.StatementCount >= 1);
    }

    [Fact]
    public void AnalyzeIfStructure_ForHeadIf_ExposesElseIfTail()
    {
        var source = AtomicControlFlowSources.IfElseIfElseSource;
        var (context, root) = CreateAnalysisContext(source, "if-structure-head.cs");
        var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().First();

        var analysis = new IfStructureAnalyzer().Analyze(ifStatement, context);

        Assert.Equal(IfStructureVariant.HeadIf, analysis.AnchorVariant);
        Assert.Equal(IfSectionKind.If, analysis.AnchorSection.Kind);
        Assert.NotNull(analysis.TailSection);
        Assert.Equal(IfSectionKind.ElseIf, analysis.TailSection!.Kind);
    }

    [Fact]
    public void AnalyzeIfStructure_ForElseIf_UsesElseIfVariant()
    {
        var source = AtomicControlFlowSources.ElseIfElseSource;
        var (context, root) = CreateAnalysisContext(source, "if-structure-elseif.cs");
        var elseIfStatement = root.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Single(node => node.Parent is ElseClauseSyntax);

        var analysis = new IfStructureAnalyzer().Analyze(elseIfStatement, context);

        Assert.Equal(IfStructureVariant.ElseIf, analysis.AnchorVariant);
        Assert.Equal(IfSectionKind.ElseIf, analysis.AnchorSection.Kind);
        Assert.NotNull(analysis.ParentElseClause);
        Assert.NotNull(analysis.TailSection);
        Assert.Equal(IfSectionKind.Else, analysis.TailSection!.Kind);
    }

    private static (CpgAnalysisContext Context, SyntaxNode Root) CreateAnalysisContext(string source, string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var compilation = CSharpCompilation.Create(
            "StructureViewBuilderTests",
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            });
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPGBuilder().BuildFromSource(source, filePath);
        return (new CpgAnalysisContext(graph, semanticModel, root), root);
    }

    private static NLCPGStructureView BuildStructuralView(
        IReadOnlyCollection<SyntaxNode> fragments,
        CpgAnalysisContext context)
    {
        return new NLCPGStructureViewBuilder().Build(
            fragments,
            context,
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Bidirectional,
            new NLCPGTraversalBudget(16, 1, 1, 4096, 8192));
    }

    private static HashSet<NodeId> ResolveGraphNodeIdsInside(CpgAnalysisContext context, SyntaxNode syntaxNode)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        return context.Graph.Nodes
            .Where(node =>
                string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
                node.SpanStart >= syntaxNode.SpanStart &&
                node.SpanEnd <= syntaxNode.Span.End)
            .Select(node => node.NodeId!.Value)
            .ToHashSet();
    }

    private static bool HasUndirectedPath(NLCPGStructureView view, IReadOnlySet<NodeId> sourceNodeIds, IReadOnlySet<NodeId> targetNodeIds)
    {
        var adjacency = new Dictionary<NodeId, List<NodeId>>();
        foreach (var edge in view.Edges)
        {
            AddNeighbor(adjacency, edge.SourceNodeId, edge.TargetNodeId);
            AddNeighbor(adjacency, edge.TargetNodeId, edge.SourceNodeId);
        }

        var queue = new Queue<NodeId>(sourceNodeIds.Where(id => view.Nodes.Any(node => node.NodeId == id)));
        var visited = queue.ToHashSet();
        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (targetNodeIds.Contains(nodeId))
            {
                return true;
            }

            if (!adjacency.TryGetValue(nodeId, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return false;
    }

    private static void AddNeighbor(IDictionary<NodeId, List<NodeId>> adjacency, NodeId sourceId, NodeId targetId)
    {
        if (!adjacency.TryGetValue(sourceId, out var neighbors))
        {
            neighbors = new List<NodeId>();
            adjacency[sourceId] = neighbors;
        }

        neighbors.Add(targetId);
    }

    private static string[] ResolveGraphEdgeKeysInside(CpgAnalysisContext context, IReadOnlySet<NodeId> nodeIds)
    {
        return context.Graph.Edges
            .Where(edge => nodeIds.Contains(edge.SourceNodeId) && nodeIds.Contains(edge.TargetNodeId))
            .Select(FormatEdgeKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] ResolveViewEdgeKeys(NLCPGStructureView view)
    {
        return view.Edges
            .Select(FormatEdgeKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatEdgeKey(NLCPGEdge edge)
    {
        return $"{edge.SourceNodeId}|{edge.Kind}|{edge.StructuredLabel?.StableKey}|{edge.TargetNodeId}";
    }

    private static NLCPGNode CreateLabeledNode(string name)
    {
        return new NLCPGNode(NLCPGNodeKind.Operation, "Operation", Name: name);
    }

    private static string BuildNodeContractKey(NLCPGNode node)
    {
        return string.Join(
            "|",
            node.Kind,
            node.DisplayKind,
            node.Name,
            node.FullName,
            node.Signature,
            node.FilePath,
            node.SpanStart,
            node.SpanEnd,
            node.IsImplicit);
    }

    private static NLCPGNode FindNode(NLCPGGraph graph, string displayId)
    {
        return Assert.Single(graph.Nodes, node => node.Name == displayId || node.FullName == displayId);
    }

    private static NodeId RequireNodeId(NLCPGNode node)
    {
        return Assert.NotNull(node.NodeId);
    }
}
