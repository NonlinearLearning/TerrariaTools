using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Context;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;
using TerrariaTools.UnitTests.Infrastructure;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

public class HybridContextQueryApiTests : RoslynTestBase
{
    [Fact]
    public async Task IsVariableDefined_ShouldReturnTrue_ForLocalAndParameter()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridContextQueryApiTests_Source_1;

        var context = await AnalyzeContextAsync(source);

        Assert.True(context.IsVariableDefined("x"));
        Assert.True(context.IsVariableDefined("p"));
        Assert.False(context.IsVariableDefined("not_exists"));
    }

    [Fact]
    public async Task IsVariableDefined_ShouldHandleShadowedName()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridContextQueryApiTests_Source_1;

        var context = await AnalyzeContextAsync(source);
        Assert.True(context.IsVariableDefined("x"));
    }

    [Fact]
    public async Task FindReferences_ShouldReturnAllReferences_ForLocalDeclaration()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridContextQueryApiTests_Source_1;

        var context = await AnalyzeContextAsync(source);
        var root = context.OriginalTree.GetRoot();
        var declaration = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().First(v => v.Identifier.ValueText == "x");

        var refs = context.FindReferences(declaration).OfType<IdentifierNameSyntax>().ToList();

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.Equal("x", r.Identifier.ValueText));
    }

    [Fact]
    public async Task FindReferences_ShouldReturnEmpty_WhenNoReference()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridContextQueryApiTests_Source_1;

        var context = await AnalyzeContextAsync(source);
        var root = context.OriginalTree.GetRoot();
        var declaration = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().First(v => v.Identifier.ValueText == "y");

        var refs = context.FindReferences(declaration).ToList();
        Assert.Empty(refs);
    }

    private async Task<RewriteContext> AnalyzeContextAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var engine = new HybridRewriteEngine(new RuleEngine());
        var context = engine.CreateContext(model, tree);
        _ = engine.Analyze(root, context);
        return context;
    }
}


