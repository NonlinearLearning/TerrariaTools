using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;
using TerrariaTools.UnitTests.Infrastructure;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

public class HybridAtomicMiddlewareTests : RoslynTestBase
{
    [Fact]
    public async Task Atomic_RemoveNodeMiddleware_ShouldDeleteMarkedWhile()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridAtomicMiddlewareTests_Source_1;

        var result = await RewriteWithCustomRulesAsync(
            source,
            root => root.DescendantNodes().OfType<WhileStatementSyntax>(),
            ruleEngine =>
            {
                ruleEngine.RegisterRule<WhileStatementSyntax>(rule => rule
                    .When(IsMarked)
                    .Use<RemoveNodeMiddleware<WhileStatementSyntax>>());
            });

        Assert.DoesNotContain("while", result);
        Assert.Contains("Console.WriteLine(1)", result);
    }

    [Fact]
    public async Task Atomic_ReplaceNodeMiddleware_ShouldReplaceMarkedReturn()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridAtomicMiddlewareTests_Source_1;

        var replacement = SyntaxFactory.ReturnStatement(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(42)));

        var result = await RewriteWithCustomRulesAsync(
            source,
            root => root.DescendantNodes().OfType<ReturnStatementSyntax>(),
            ruleEngine =>
            {
                ruleEngine.RegisterRule<ReturnStatementSyntax>(rule => rule
                    .When(IsMarked)
                    .Use<ReplaceNodeMiddleware<ReturnStatementSyntax>>());
            },
            context =>
            {
                context.SetState(AtomicOperationStateKeys.ReplacementNode, replacement);
            });

        Assert.Contains("return 42;", result);
        Assert.DoesNotContain("return 1;", result);
    }

    [Fact]
    public async Task Atomic_InsertBeforeAfterMiddleware_ShouldInsertStatementsAroundMarkedStatement()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridAtomicMiddlewareTests_Source_1;

        var before = SyntaxFactory.ParseStatement("""Console.WriteLine("before");""");
        var after = SyntaxFactory.ParseStatement("""Console.WriteLine("after");""");

        var result = await RewriteWithCustomRulesAsync(
            source,
            root => root.DescendantNodes().OfType<ExpressionStatementSyntax>()
                .Where(statement => statement.ToString().Contains("core")),
            ruleEngine =>
            {
                ruleEngine.RegisterRule<ExpressionStatementSyntax>(rule => rule
                    .When(IsMarked)
                    .Use<InsertBeforeMiddleware<ExpressionStatementSyntax>>()
                    .Use<InsertAfterMiddleware<ExpressionStatementSyntax>>());
            },
            context =>
            {
                context.SetState(AtomicOperationStateKeys.InsertBeforeStatements, new List<StatementSyntax> { before });
                context.SetState(AtomicOperationStateKeys.InsertAfterStatements, new List<StatementSyntax> { after });
            });

        var beforeIndex = result.IndexOf("""Console.WriteLine("before");""", StringComparison.Ordinal);
        var coreIndex = result.IndexOf("""Console.WriteLine("core");""", StringComparison.Ordinal);
        var afterIndex = result.IndexOf("""Console.WriteLine("after");""", StringComparison.Ordinal);

        Assert.True(beforeIndex >= 0);
        Assert.True(coreIndex >= 0);
        Assert.True(afterIndex >= 0);
        Assert.True(beforeIndex < coreIndex && coreIndex < afterIndex);
    }

    private async Task<string> RewriteWithCustomRulesAsync(
        string source,
        Func<SyntaxNode, IEnumerable<SyntaxNode>> markedSelector,
        Action<RuleEngine> configureRules,
        Action<TerrariaTools.RewriteCodeExpressions.Hybrid.Context.RewriteContext>? configureContext = null)
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
        configureContext?.Invoke(context);

        var rewritten = engine.Rewrite(root, context);
        return rewritten.NormalizeWhitespace().ToFullString();
    }

    private static bool IsMarked(SyntaxNode node, TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts.IRewriteContext context)
    {
        var marked = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        return marked is not null && marked.Contains(node);
    }
}


