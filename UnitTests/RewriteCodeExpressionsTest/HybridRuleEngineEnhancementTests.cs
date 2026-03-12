using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Context;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;
using TerrariaTools.UnitTests.Infrastructure;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

public class HybridRuleEngineEnhancementTests : RoslynTestBase
{
    [Fact]
    public async Task RewriteRule_ShouldSupport_AndOrNot()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridRuleEngineEnhancementTests_Source_1;

        var (ifNode, context) = await GetIfNodeAndContextAsync(source);

        var andRule = new RewriteRule<IfStatementSyntax>()
            .When((_, _) => true)
            .And((_, _) => true)
            .Not((_, _) => false);
        Assert.True(andRule.IsApplicable(ifNode, context));

        var orRule = new RewriteRule<IfStatementSyntax>()
            .When((_, _) => false)
            .Or((_, _) => true);
        Assert.True(orRule.IsApplicable(ifNode, context));

        var notRule = new RewriteRule<IfStatementSyntax>()
            .When((_, _) => true)
            .Not((_, _) => true);
        Assert.False(notRule.IsApplicable(ifNode, context));
    }

    [Fact]
    public async Task RuleEngine_ShouldPick_HighestPriorityRule()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridRuleEngineEnhancementTests_Source_1;

        var (ifNode, context) = await GetIfNodeAndContextAsync(source);
        var engine = new RuleEngine();

        engine.RegisterRule<IfStatementSyntax>(rule => rule
            .When((_, _) => true)
            .WithPriority(100)
            .Use<KeepIfMiddleware>());

        engine.RegisterRule<IfStatementSyntax>(rule => rule
            .When((_, _) => true)
            .WithPriority(10)
            .Use<KeepIfMiddleware>());

        var match = engine.FindMatchingRule(ifNode, context);
        Assert.NotNull(match);
        Assert.Equal(10, match!.Priority);
    }

    [Fact]
    public async Task RuleEngine_ShouldThrowConflict_WhenTopPriorityTies()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridRuleEngineEnhancementTests_Source_1;

        var (ifNode, context) = await GetIfNodeAndContextAsync(source);
        var engine = new RuleEngine();

        engine.RegisterRule<IfStatementSyntax>(rule => rule
            .When((_, _) => true)
            .WithPriority(10)
            .Use<KeepIfMiddleware>());

        engine.RegisterRule<IfStatementSyntax>(rule => rule
            .When((_, _) => true)
            .WithPriority(10)
            .Use<KeepIfMiddleware>());

        Assert.Throws<RuleConflictException>(() => engine.FindMatchingRule(ifNode, context));
    }

    [Fact]
    public async Task RuleEngine_ShouldValidate_MiddlewareCompatibility()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridRuleEngineEnhancementTests_Source_1;

        var (_, context) = await GetIfNodeAndContextAsync("""
public class C { public void M(){ if(true){} } }
""");
        _ = context; // keep static analyzer quiet

        var engine = new RuleEngine();
        var badRule = new BadRule();

        Assert.Throws<InvalidOperationException>(() => engine.RegisterRule(badRule));
    }

    [Fact]
    public async Task CommonConditions_ShouldWork()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridRuleEngineEnhancementTests_Source_1;

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        var context = new RewriteContext(model, tree);

        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var methodDecl = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var assignExpr = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().First();

        Assert.True(CommonConditions.IsAssignment(assignExpr));
        Assert.True(CommonConditions.HasAttribute(classDecl, "Obsolete"));
        Assert.True(CommonConditions.IsPublic(classDecl));
        Assert.True(CommonConditions.IsPublic(methodDecl));
        Assert.True(CommonConditions.Implements(classDecl, "IFoo", context));
    }

    private async Task<(IfStatementSyntax ifNode, RewriteContext context)> GetIfNodeAndContextAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        var ifNode = root.DescendantNodes().OfType<IfStatementSyntax>().First();
        return (ifNode, new RewriteContext(model, tree));
    }

    private sealed class KeepIfMiddleware : IMiddleware<IfStatementSyntax>
    {
        public SyntaxNode Invoke(IfStatementSyntax node, IRewriteContext context, MiddlewareDelegate<IfStatementSyntax> next)
        {
            return next(node, context);
        }
    }

    private sealed class BadRule : IRule
    {
        public string Name => "BadRule";
        public Type NodeType => typeof(WhileStatementSyntax);
        public int Priority => 0;
        public bool IsApplicable(SyntaxNode node, IRewriteContext context) => true;
        public IEnumerable<Type> GetMiddlewareTypes() => new[] { typeof(KeepIfMiddleware) };
    }
}


