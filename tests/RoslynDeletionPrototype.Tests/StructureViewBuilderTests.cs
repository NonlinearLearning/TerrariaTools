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
    public void Build_ForMemberAccess_ReusesExistingGraphNodeAndAddsReceiverEdge()
    {
        var source = SObjectExpressionSources.ReturnExpressionSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-member-access.cs");
        var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single();

        var view = new RoslynCpgStructureViewBuilder().Build(memberAccess, context);

        Assert.False(view.Root.Id.StartsWith("structure:", StringComparison.Ordinal));
        Assert.Contains(view.Root.Kind, new[]
        {
            RoslynCpgNodeKind.MemberAccess,
            RoslynCpgNodeKind.CallSite
        });
        Assert.Contains(view.Edges, edge =>
            edge.SourceId == view.Root.Id &&
            edge.Kind == RoslynCpgEdgeKind.OpInstance &&
            string.Equals(edge.Label, "receiver", StringComparison.Ordinal));
        Assert.Contains(view.Edges, edge =>
            edge.SourceId == view.Root.Id &&
            edge.Kind == RoslynCpgEdgeKind.AccessesMember &&
            string.Equals(edge.Label, "member", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ForAssignmentDefinition_AddsInitializerDataFlowWithoutSelfEdge()
    {
        var source = SObjectExpressionSources.TargetNameSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-assignment-definition.cs");
        var declarator = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();

        var view = new RoslynCpgStructureViewBuilder().Build(declarator, context);

        Assert.Contains(view.Root.Kind, new[]
        {
            RoslynCpgNodeKind.OpAssignment,
            RoslynCpgNodeKind.Operation
        });
        Assert.Contains(view.Edges, edge =>
            edge.TargetId == view.Root.Id &&
            edge.Kind == RoslynCpgEdgeKind.DataFlow &&
            string.Equals(edge.Label, "initializer-to-definition", StringComparison.Ordinal));
        Assert.DoesNotContain(view.Edges, edge =>
            edge.SourceId == view.Root.Id &&
            edge.TargetId == view.Root.Id);
    }

    [Fact]
    public void Build_ForWhileStatement_UsesConditionAndBodyEdges()
    {
        var source = SObjectControlFlowSources.WhileConditionSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-while.cs");
        var whileStatement = root.DescendantNodes().OfType<WhileStatementSyntax>().Single();

        var view = new RoslynCpgStructureViewBuilder().Build(whileStatement, context);

        Assert.Contains(view.Root.Kind, new[]
        {
            RoslynCpgNodeKind.OpLoop,
            RoslynCpgNodeKind.SyntaxNode
        });
        Assert.Contains(view.Edges, edge =>
            edge.SourceId == view.Root.Id &&
            edge.Kind == RoslynCpgEdgeKind.OpCondition &&
            string.Equals(edge.Label, "condition", StringComparison.Ordinal));
        Assert.Contains(view.Edges, edge =>
            edge.SourceId == view.Root.Id &&
            edge.Kind == RoslynCpgEdgeKind.OpBody &&
            string.Equals(edge.Label, "body", StringComparison.Ordinal));
        Assert.DoesNotContain(view.Edges, edge =>
            edge.SourceId == view.Root.Id &&
            edge.TargetId == view.Root.Id);
    }

    [Fact]
    public void Build_ForLogicalAndExpression_CollectsOperandChildrenWithoutSelfEdge()
    {
        var source = SObjectLogicalSources.LogicalAndConditionSource;
        var (context, root) = CreateAnalysisContext(source, "structure-view-logical-and.cs");
        var binaryExpression = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Single(node => node.IsKind(SyntaxKind.LogicalAndExpression));

        var view = new RoslynCpgStructureViewBuilder().Build(binaryExpression, context);

        Assert.Equal(RoslynCpgNodeKind.OpBinary, view.Root.Kind);
        Assert.Contains(view.Nodes, node => node.Text?.Contains("ready", StringComparison.Ordinal) == true);
        Assert.Contains(view.Nodes, node => node.Text?.Contains("s.IsReady", StringComparison.Ordinal) == true);
        Assert.Contains(view.Edges, edge =>
            edge.SourceId == view.Root.Id &&
            edge.Kind == RoslynCpgEdgeKind.OpChild);
        Assert.DoesNotContain(view.Edges, edge =>
            edge.SourceId == view.Root.Id &&
            edge.TargetId == view.Root.Id);
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
}
