using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Testing.Contracts;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rewrite;

public sealed class RoslynRewriteExecutorContractTests
{
    [Fact]
    public async Task Executor_SatisfiesRewriteContract()
    {
        await RewriteExecutorContract.AssertReturnsConfiguredResultAsync(new RoslynRewriteExecutor());
    }

    [Fact]
    public async Task Executor_SucceedsForEmptyPlan()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { } }", path: "Sample.cs");
        var root = tree.GetCompilationUnitRoot();
        var compilation = CSharpCompilation.Create(
            "RoslynRewriteExecutorContractTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var context = new RewriteExecutionDocumentContext(
            new SourceDocument("Sample.cs", "Sample.cs", tree.ToString()),
            root,
            compilation.GetSemanticModel(tree));
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "in", "out", RunMode.Standard),
            Array.Empty<PlannedChange>(),
            Array.Empty<PlanConflict>());

        var result = await new RoslynRewriteExecutor().ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("void M()", result.RewrittenSource);
    }
}
