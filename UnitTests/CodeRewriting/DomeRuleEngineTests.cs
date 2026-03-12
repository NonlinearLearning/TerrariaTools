using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Rules.Dome;
using TerrariaTools.Rules.Dome.Mark;
using TerrariaTools.Rules.Dome.Mark.StaticRules;
using TerrariaTools.Rules.Dome.Rewrite;
using TerrariaTools.UnitTests.Infrastructure;

namespace TerrariaTools.UnitTests.CodeRewriting;

public class DomeRuleEngineTests : RoslynTestBase
{
    [Fact]
    public async Task DataFlowAnalyzer_DoesNotTreatAssignedMemberAsUse()
    {
        const string source = """
using System;

public class Sample
{
    public int Value { get; set; }

    public void Update()
    {
        Value = 1;
    }
}
""";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        var analyzer = new DataFlowDependencyAnalyzer(model);
        analyzer.Analyze(root);

        var propertyDeclaration = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var propertySymbol = model.GetDeclaredSymbol(propertyDeclaration)!;
        var statement = root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        var variableNode = analyzer.Graph.GetNode(propertySymbol);
        Assert.NotNull(variableNode);

        var falseUseEdges = analyzer.Graph.OutEdges(variableNode!)
            .Where(edge => edge.Kind == DataFlowDependencyEdgeKind.Uses && edge.Target.Syntax == statement)
            .ToList();

        Assert.Empty(falseUseEdges);
    }

    [Fact]
    public async Task FunctionMarkingRule_AddReturnAnnotation_IsRewrittenIntoReturnStatement()
    {
        const string source = """
public class Sample
{
    public int Compute()
    {
    }
}

public class Caller
{
    public int Invoke(Sample sample) => sample.Compute();
}
""";

        var (workspace, solution, project) = await CreateSolutionAsync(("Code.cs", source));
        try
        {
            var document = project.Documents.Single();
            var root = await document.GetSyntaxRootAsync();
            var model = await document.GetSemanticModelAsync();
            var method = root!.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Compute");

            var rule = new FunctionMarkingRule();
            var markedMethod = await rule.MarkMethodAsync(method, model!, solution);

            var rewrittenRoot = root.ReplaceNode(method, markedMethod);
            var output = new CodeRewriter().Apply(rewrittenRoot).NormalizeWhitespace().ToFullString();

            Assert.Contains("return 0;", output);
        }
        finally
        {
            workspace.Dispose();
        }
    }

    [Fact]
    public async Task RuleEngine_UsesFrameworkReferencesWithoutCompilationErrors()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading.Tasks;

public class Sample
{
    public async Task<int> ComputeAsync()
    {
        var values = new List<int> { 1, 2, 3 };
        return await Task.FromResult(values.Count);
    }
}
""";

        var root = await AnnotateFirstStatementAsync(source);
        var engine = new RuleEngine();
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            _ = engine.Apply(root);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.DoesNotContain("[RuleEngine Compilation Error]", writer.ToString());
    }

    [Fact]
    public async Task RuleEngine_CommentOutsFieldAssignmentsInsteadOfDeletingThem()
    {
        const string source = """
public class Sample
{
    private int _counter;

    public void Update()
    {
        _counter = 42;
    }
}
""";

        var root = await AnnotateFirstStatementAsync(source);

        var annotated = new RuleEngine().Apply(root);
        var rewritten = new CodeRewriter().Apply(annotated).NormalizeWhitespace().ToFullString();

        Assert.Contains("[Commented:", rewritten);
        Assert.Contains("_counter = 0;", rewritten);
    }

    [Fact]
    public async Task RuleEngine_RewritesMarkedObjectInitializerAssignmentsToDefaultValues()
    {
        const string source = """
public class Payload
{
    public string Name { get; set; }
}

public class Sample
{
    public void Update(string input)
    {
        var payload = new Payload { Name = input };
    }
}
""";

        var root = await AnnotateFirstStatementAsync(source);

        var annotated = new RuleEngine().Apply(root);
        var rewritten = new CodeRewriter().Apply(annotated).NormalizeWhitespace().ToFullString();

        Assert.Contains("new Payload() { Name = null }", rewritten);
        Assert.DoesNotContain("[Deleted:", rewritten);
    }

    private async Task<SyntaxNode> AnnotateFirstStatementAsync(string source)
    {
        var (root, _) = await GetCompilationAsync(source);
        var statement = root.DescendantNodes().OfType<StatementSyntax>().First();
        return root.ReplaceNode(
            statement,
            statement.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.SourceAnnotationKind, "test")));
    }
}
