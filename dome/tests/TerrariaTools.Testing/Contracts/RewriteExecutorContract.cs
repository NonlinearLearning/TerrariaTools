using TerrariaTools.Dome.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace TerrariaTools.Testing.Contracts;

public static class RewriteExecutorContract
{
    public static async Task AssertReturnsConfiguredResultAsync(IRewriteExecutor executor)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { } }", path: "Sample.cs");
        var root = tree.GetCompilationUnitRoot();
        var compilation = CSharpCompilation.Create(
            "RewriteExecutorContract",
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

        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.NotNull(result);
    }
}
