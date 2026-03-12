using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Context;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;
using TerrariaTools.UnitTests.Infrastructure;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

public class HybridMruPlanningTests : RoslynTestBase
{
    [Fact]
    public async Task MruPlanning_ShouldStopChildMatching_WhenParentMruMatched()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridMruPlanningTests_Source_1;

        var plan = await AnalyzeWithRulesAsync(source, ruleEngine =>
        {
            ruleEngine.RegisterRule<IfStatementSyntax>(rule => rule
                .When((_, _) => true)
                .WithPriority(10)
                .Use<NoOpIfMiddleware>());

            ruleEngine.RegisterRule<ExpressionStatementSyntax>(rule => rule
                .When((_, _) => true)
                .WithPriority(20)
                .Use<NoOpExpressionStatementMiddleware>());
        });

        Assert.Single(plan.Items);
        Assert.IsType<IfStatementSyntax>(plan.Items[0].Node);
    }

    [Fact]
    public async Task MruPlanning_ShouldMatchChild_WhenParentNotMatched()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridMruPlanningTests_Source_1;

        var plan = await AnalyzeWithRulesAsync(source, ruleEngine =>
        {
            ruleEngine.RegisterRule<IfStatementSyntax>(rule => rule
                .When((_, _) => false)
                .WithPriority(10)
                .Use<NoOpIfMiddleware>());

            ruleEngine.RegisterRule<ExpressionStatementSyntax>(rule => rule
                .When((_, _) => true)
                .WithPriority(20)
                .Use<NoOpExpressionStatementMiddleware>());
        });

        Assert.Single(plan.Items);
        Assert.IsType<ExpressionStatementSyntax>(plan.Items[0].Node);
    }

    [Fact]
    public async Task MruPlanning_ShouldTraverseDeepCompositeStructure()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridMruPlanningTests_Source_1;

        var plan = await AnalyzeWithRulesAsync(source, ruleEngine =>
        {
            ruleEngine.RegisterRule<WhileStatementSyntax>(rule => rule
                .When((_, _) => true)
                .WithPriority(10)
                .Use<NoOpWhileMiddleware>());
        });

        Assert.Single(plan.Items);
        Assert.IsType<WhileStatementSyntax>(plan.Items[0].Node);
    }

    private async Task<TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis.RewritePlan> AnalyzeWithRulesAsync(
        string source,
        Action<RuleEngine> configureRules)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var ruleEngine = new RuleEngine();
        configureRules(ruleEngine);
        var engine = new HybridRewriteEngine(ruleEngine);
        var context = engine.CreateContext(model, tree);

        return engine.Analyze(root, context);
    }

    private sealed class NoOpIfMiddleware : IMiddleware<IfStatementSyntax>
    {
        public SyntaxNode Invoke(IfStatementSyntax node, IRewriteContext context, MiddlewareDelegate<IfStatementSyntax> next)
            => next(node, context);
    }

    private sealed class NoOpExpressionStatementMiddleware : IMiddleware<ExpressionStatementSyntax>
    {
        public SyntaxNode Invoke(ExpressionStatementSyntax node, IRewriteContext context, MiddlewareDelegate<ExpressionStatementSyntax> next)
            => next(node, context);
    }

    private sealed class NoOpWhileMiddleware : IMiddleware<WhileStatementSyntax>
    {
        public SyntaxNode Invoke(WhileStatementSyntax node, IRewriteContext context, MiddlewareDelegate<WhileStatementSyntax> next)
            => next(node, context);
    }
}

