using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Pipeline;
using TerrariaTools.UnitTests.Infrastructure;
using TerrariaTools.UnitTests.Scenarios;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

public class HybridPipelineIntegrationTests : RoslynTestBase
{
    [Fact]
    public async Task HybridPipeline_ShouldRemoveMarkedWhileStatement()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.WhileLoop);

        var (result, _) = await RunHybridAsync(
            source,
            root => root.DescendantNodes().OfType<WhileStatementSyntax>());

        Assert.DoesNotContain("while", result);
        Assert.Contains("Console.WriteLine", result);
    }

    [Fact]
    public async Task HybridPipeline_ShouldReportMetrics()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.IfElse);

        var (_, metrics) = await RunHybridAsync(
            source,
            root => root.DescendantNodes().OfType<IfStatementSyntax>().Take(1));

        Assert.True(metrics.PlanItemCount > 0);
        Assert.True(metrics.ExecutedRuleCount > 0);
        Assert.True(metrics.ReplacedNodeCount + metrics.DeletedNodeCount > 0);
    }

    [Fact]
    public async Task HybridPipeline_ShouldApplyTerrariaConditionsWithoutExplicitMarks()
    {
        var source = SharedScenarios.TerrariaConditions.IfElsePromote;
        var conditions = new[]
        {
            new RewriteCondition
            {
                SymbolName = "netMode",
                Operator = SyntaxKind.EqualsExpression,
                Value = "1",
                IsValueLiteral = true
            }
        };

        var (result, _) = await RunHybridAsync(
            source,
            _ => Enumerable.Empty<SyntaxNode>(),
            conditions);

        Assert.Contains("ServerOnly", result);
        Assert.DoesNotContain("ClientOnly", result);
        Assert.DoesNotContain("if", result);
    }

    [Fact]
    public async Task HybridPipeline_ShouldMatchClassicPipeline_ForLogicalAndScenario()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.LogicalAnd);

        var classic = await RunClassicAsync(
            source,
            root =>
            {
                var binary = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                    .First(b => b.Kind() == SyntaxKind.LogicalAndExpression);
                return new SyntaxNode[] { binary.Left };
            });

        var (hybrid, _) = await RunHybridAsync(
            source,
            root =>
            {
                var binary = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                    .First(b => b.Kind() == SyntaxKind.LogicalAndExpression);
                return new SyntaxNode[] { binary.Left };
            });

        Assert.Equal(classic, hybrid);
    }

    [Fact]
    public async Task HybridPipeline_ShouldSimplifyBooleanExpressions()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(@"
            bool x = true;
            if (true && !!x || false) { System.Console.WriteLine(1); }");

        var (result, _) = await RunHybridAsync(source, _ => Enumerable.Empty<SyntaxNode>());

        Assert.DoesNotContain("true &&", result);
        Assert.DoesNotContain("!!x", result);
    }

    private async Task<(string result, HybridRewriteMetrics metrics)> RunHybridAsync(
        string source,
        Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes,
        IEnumerable<RewriteCondition>? terrariaConditions = null)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        var nodesToMark = new HashSet<SyntaxNode>(selectNodes(root));
        HybridRewriteMetrics? metrics = null;

        var rewritten = await PipelineExpressionSimplifier.RewriteAsync(
            root,
            model,
            solution: null,
            predicate: _ => false,
            nodesToMark: nodesToMark,
            globalMethodActions: null,
            cancellationToken: default,
            traceContext: null,
            useHybrid: true,
            terrariaConditions: terrariaConditions,
            hybridMetricsSink: m => metrics = m);

        return (
            rewritten.NormalizeWhitespace().ToFullString(),
            metrics ?? new HybridRewriteMetrics(0, 0, 0, 0));
    }

    private async Task<string> RunClassicAsync(
        string source,
        Func<SyntaxNode, IEnumerable<SyntaxNode>> selectNodes)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        var nodesToMark = new HashSet<SyntaxNode>(selectNodes(root));

        var rewritten = await PipelineExpressionSimplifier.RewriteAsync(
            root,
            model,
            solution: null,
            predicate: _ => false,
            nodesToMark: nodesToMark);

        return rewritten.NormalizeWhitespace().ToFullString();
    }
}
