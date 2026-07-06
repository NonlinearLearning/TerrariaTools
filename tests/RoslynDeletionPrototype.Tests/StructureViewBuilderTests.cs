using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class StructureViewBuilderTests
{
    [Fact]
    public void Build_ForSingleFragment_CopiesMainGraphNodesAndEdgesInsideFragment()
    {
        var source = SObjectExpressionSources.ReturnExpressionSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-single-fragment.cs");
        var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single();
        var expectedNodeIds = ResolveGraphNodeIdsInside(context, memberAccess);

        var view = new RoslynCpgStructureViewBuilder().Build(memberAccess, context);

        Assert.NotEmpty(expectedNodeIds);
        Assert.True(expectedNodeIds.IsSubsetOf(view.Nodes.Select(node => node.Id)));
        Assert.All(view.Edges, edge => Assert.Contains(edge, context.Graph.Edges));
    }

    [Fact]
    public void Build_ForMultipleFragments_CopiesMainGraphNodesAndConnectsFragments()
    {
        var source = SObjectExpressionSources.TargetNameSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-multiple-fragments.cs");
        var declarator = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.ToString() == "s.Seed");
        var declaratorNodeIds = ResolveGraphNodeIdsInside(context, declarator);
        var memberAccessNodeIds = ResolveGraphNodeIdsInside(context, memberAccess);

        var view = new RoslynCpgStructureViewBuilder().Build(new SyntaxNode[] { declarator, memberAccess }, context);

        Assert.NotEmpty(declaratorNodeIds);
        Assert.NotEmpty(memberAccessNodeIds);
        Assert.True(declaratorNodeIds.IsSubsetOf(view.Nodes.Select(node => node.Id)));
        Assert.True(memberAccessNodeIds.IsSubsetOf(view.Nodes.Select(node => node.Id)));
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

        var view = new RoslynCpgStructureViewBuilder().Build(
            new SyntaxNode[] { firstLiteral, secondLiteral },
            context);

        Assert.True(HasUndirectedPath(view, firstNodeIds, secondNodeIds));
        Assert.True(view.Nodes.Count > firstNodeIds.Union(secondNodeIds).Count());
        Assert.All(view.Edges, edge => Assert.Contains(edge, context.Graph.Edges));
    }

    [Fact]
    public void AnalyzeBinaryExpression_ReturnsAffectedSyntaxTreeOnly()
    {
        var source = SObjectLogicalSources.LogicalAndConditionSource;
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
        var source = SObjectExpressionSources.TargetNameSource;
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
        var source = SObjectLogicalSources.LogicalAndConditionSource;
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
        var source = SObjectControlFlowSources.IfElseIfElseSource;
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
        var source = SObjectControlFlowSources.ElseIfElseSource;
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

    private static (CpgAnalysisContext Context, SyntaxNode Root) CreateAnalysisContext(
        string source,
        string filePath)
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
        var graph = new RoslynCpgBuilder().BuildFromSource(source, filePath);
        return (new CpgAnalysisContext(graph, semanticModel, root), root);
    }

    private static HashSet<string> ResolveGraphNodeIdsInside(CpgAnalysisContext context, SyntaxNode syntaxNode)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        return context.Graph.Nodes
            .Where(node =>
                string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
                node.SpanStart >= syntaxNode.SpanStart &&
                node.SpanEnd <= syntaxNode.Span.End)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasUndirectedPath(
        RoslynCpgStructureView view,
        IReadOnlySet<string> sourceNodeIds,
        IReadOnlySet<string> targetNodeIds)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in view.Edges)
        {
            AddNeighbor(adjacency, edge.SourceId, edge.TargetId);
            AddNeighbor(adjacency, edge.TargetId, edge.SourceId);
        }

        var queue = new Queue<string>(sourceNodeIds.Where(id => view.Nodes.Any(node => node.Id == id)));
        var visited = queue.ToHashSet(StringComparer.Ordinal);
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

    private static void AddNeighbor(
        IDictionary<string, List<string>> adjacency,
        string sourceId,
        string targetId)
    {
        if (!adjacency.TryGetValue(sourceId, out var neighbors))
        {
            neighbors = new List<string>();
            adjacency[sourceId] = neighbors;
        }

        neighbors.Add(targetId);
    }
}
