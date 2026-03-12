using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;
using TerrariaTools.UnitTests.Infrastructure;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

public class HybridUtilityMiddlewareTests : RoslynTestBase
{
    [Fact]
    public async Task PreserveTriviaMiddleware_ShouldKeepLeadingAndTrailingTrivia()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridUtilityMiddlewareTests_Source_1;

        var result = await RewriteWithCustomRulesAsync(
            source,
            root => root.DescendantNodes().OfType<ExpressionStatementSyntax>(),
            ruleEngine =>
            {
                ruleEngine.RegisterRule<ExpressionStatementSyntax>(rule => rule
                    .When(IsMarked)
                    .Use<PreserveTriviaMiddleware<ExpressionStatementSyntax>>()
                    .Use<ReplaceExpressionStatementMiddleware>());
            });

        Assert.Contains("// keep-me", result);
        Assert.Contains("// tail", result);
        Assert.Contains("Console.WriteLine(2);", result);
    }

    [Fact]
    public async Task FormatNodeMiddleware_ShouldNormalizeWhitespace()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridUtilityMiddlewareTests_Source_1;

        var result = await RewriteWithCustomRulesAsync(
            source,
            root => root.DescendantNodes().OfType<ReturnStatementSyntax>(),
            ruleEngine =>
            {
                ruleEngine.RegisterRule<ReturnStatementSyntax>(rule => rule
                    .When(IsMarked)
                    .Use<FormatNodeMiddleware<ReturnStatementSyntax>>()
                    .Use<MessyReturnMiddleware>());
            });

        Assert.Contains("return 2 + 3;", result);
    }

    [Fact]
    public async Task LogMetricMiddleware_ShouldCountRuleHits()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridUtilityMiddlewareTests_Source_1;

        Dictionary<string, int>? hits = null;
        _ = await RewriteWithCustomRulesAsync(
            source,
            root => root.DescendantNodes().OfType<ExpressionStatementSyntax>(),
            ruleEngine =>
            {
                ruleEngine.RegisterRule<ExpressionStatementSyntax>(rule => rule
                    .When(IsMarked)
                    .Use<LogMetricMiddleware<ExpressionStatementSyntax>>()
                    .Use<KeepExpressionStatementMiddleware>());
            },
            context =>
            {
                hits = context.GetState<Dictionary<string, int>>(HybridMetricsStateKeys.RuleHitCounts);
            });

        Assert.NotNull(hits);
        Assert.True(hits!.TryGetValue("ExpressionStatementSyntax:LogMetricMiddleware`1", out var count));
        Assert.Equal(2, count);
    }

    private async Task<string> RewriteWithCustomRulesAsync(
        string source,
        Func<SyntaxNode, IEnumerable<SyntaxNode>> markedSelector,
        Action<RuleEngine> configureRules,
        Action<TerrariaTools.RewriteCodeExpressions.Hybrid.Context.RewriteContext>? afterRewrite = null)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        var marked = new HashSet<SyntaxNode>(markedSelector(root));

        var ruleEngine = new RuleEngine();
        configureRules(ruleEngine);

        var engine = new HybridRewriteEngine(ruleEngine);
        var context = engine.CreateContext(model, tree);
        context.SetState(HybridInputStateKeys.MarkedNodes, marked);

        var rewritten = engine.Rewrite(root, context);
        afterRewrite?.Invoke(context);
        return rewritten.NormalizeWhitespace().ToFullString();
    }

    private static bool IsMarked(SyntaxNode node, IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }

    private sealed class ReplaceExpressionStatementMiddleware : IMiddleware<ExpressionStatementSyntax>
    {
        public SyntaxNode Invoke(ExpressionStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ExpressionStatementSyntax> next)
        {
            _ = next(node, context);
            return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("Console"),
                        SyntaxFactory.IdentifierName("WriteLine")),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(
                                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(2)))))));
        }
    }

    private sealed class MessyReturnMiddleware : IMiddleware<ReturnStatementSyntax>
    {
        public SyntaxNode Invoke(ReturnStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ReturnStatementSyntax> next)
        {
            _ = next(node, context);
            return SyntaxFactory.ParseStatement("return  2+3 ;") as ReturnStatementSyntax ?? node;
        }
    }

    private sealed class KeepExpressionStatementMiddleware : IMiddleware<ExpressionStatementSyntax>
    {
        public SyntaxNode Invoke(ExpressionStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ExpressionStatementSyntax> next)
        {
            return next(node, context);
        }
    }
}


