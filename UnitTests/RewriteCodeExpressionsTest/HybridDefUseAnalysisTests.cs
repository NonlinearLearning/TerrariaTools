using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;
using TerrariaTools.UnitTests.Infrastructure;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

public class HybridDefUseAnalysisTests : RoslynTestBase
{
    [Fact]
    public async Task DefUse_ShouldBuild_ForLocalVariable()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridDefUseAnalysisTests_Source_1;

        var (graph, _) = await AnalyzeAsync(source);
        var defs = graph.Nodes.Where(n => n.Kind == DefUseNodeKind.Definition && n.Symbol.Name == "x").ToList();
        var uses = graph.Nodes.Where(n => n.Kind == DefUseNodeKind.Use && n.Symbol.Name == "x").ToList();

        Assert.Single(defs);
        Assert.NotEmpty(uses);
        Assert.All(uses, use => Assert.True(graph.HasEdge(defs[0], use)));
    }

    [Fact]
    public async Task DefUse_ShouldBuild_ForParameter()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridDefUseAnalysisTests_Source_1;

        var (graph, _) = await AnalyzeAsync(source);
        var defs = graph.Nodes.Where(n => n.Kind == DefUseNodeKind.Definition && n.Symbol.Name == "p").ToList();
        var uses = graph.Nodes.Where(n => n.Kind == DefUseNodeKind.Use && n.Symbol.Name == "p").ToList();

        Assert.Single(defs);
        Assert.NotEmpty(uses);
        Assert.All(uses, use => Assert.True(graph.HasEdge(defs[0], use)));
    }

    [Fact]
    public async Task DefUse_ShouldMark_UnusedDefinition()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridDefUseAnalysisTests_Source_1;

        var (graph, context) = await AnalyzeAsync(source);
        var unused = context.GetState<IReadOnlyList<DefUseNode>>(AnalysisStateKeys.UnusedDefinitions);

        Assert.NotNull(unused);
        Assert.Contains(unused!, node => node.Kind == DefUseNodeKind.Definition && node.Symbol.Name == "y");
        Assert.Contains(graph.Nodes, node => node.Kind == DefUseNodeKind.Definition && node.Symbol.Name == "y");
    }

    [Fact]
    public async Task DefUse_ShouldNotCreate_NewDef_ForAssignmentLhs_InV1()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridDefUseAnalysisTests_Source_1;

        var (graph, _) = await AnalyzeAsync(source);
        var defs = graph.Nodes.Where(n => n.Kind == DefUseNodeKind.Definition && n.Symbol.Name == "x").ToList();
        Assert.Single(defs);
    }

    [Fact]
    public async Task DefUse_ShouldSeparate_ShadowedSymbols_BySymbolIdentity()
    {
        const string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.HybridDefUseAnalysisTests_Source_1;

        var (graph, _) = await AnalyzeAsync(source);
        var defs = graph.Nodes.Where(n => n.Kind == DefUseNodeKind.Definition && n.Symbol.Name == "x").ToList();
        var uses = graph.Nodes.Where(n => n.Kind == DefUseNodeKind.Use && n.Symbol.Name == "x").ToList();

        Assert.Equal(2, defs.Count);
        Assert.Equal(2, uses.Count);

        Assert.All(uses, use =>
        {
            var incomingDefs = graph.GetIncomingDefinitions(use);
            Assert.NotEmpty(incomingDefs);
            Assert.All(incomingDefs, def => Assert.True(SymbolEqualityComparer.Default.Equals(def.Symbol, use.Symbol)));
        });
    }

    private async Task<(DefUseGraph graph, TerrariaTools.RewriteCodeExpressions.Hybrid.Context.RewriteContext context)> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var engine = new HybridRewriteEngine(new RuleEngine());
        var context = engine.CreateContext(model, tree);
        _ = engine.Analyze(root, context);

        var graph = context.GetState<DefUseGraph>(AnalysisStateKeys.DefUseGraph);
        Assert.NotNull(graph);
        return (graph!, context);
    }
}


