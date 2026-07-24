using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Decision;
using RoslynPrototype.Rewrite;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

public sealed class RewritePerformanceRegressionTests
{
    private readonly ITestOutputHelper _output;

    public RewritePerformanceRegressionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void RewritePlan_FixedSource_CollectsThreeEquivalentCompilableSamples()
    {
        var source = CreateFixedSource(referenceCount: 128);

        _ = Measure(source);
        var baseline = Measure(source);
        var samples = Enumerable.Range(0, 3).Select(_ => Measure(source)).ToArray();

        AssertCompilable(baseline.RewrittenSource);
        foreach (var sample in samples)
        {
            Assert.Equal(baseline.RewrittenSource, sample.RewrittenSource);
            Assert.Equal(baseline.Edits, sample.Edits);
            Assert.Equal(baseline.Diff, sample.Diff);
            Assert.True(sample.AllocatedBytes >= 0);
            AssertCompilable(sample.RewrittenSource);
        }

        _output.WriteLine(
            $"rewrite samples buildMs={string.Join(',', samples.Select(sample => sample.BuildPlanMilliseconds))}; " +
            $"executeMs={string.Join(',', samples.Select(sample => sample.ExecutePlanMilliseconds))}; " +
            $"allocatedBytes={string.Join(',', samples.Select(sample => sample.AllocatedBytes))}; " +
            $"operations={baseline.OperationCount}");
    }

    private static RewriteMeasurement Measure(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "RewritePerformance.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var decisions = root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(node => node.Identifier.ValueText == "value")
            .Select(node => new RuleDecision(
                node,
                node,
                DecisionActionKind.Delete,
                "Replace benchmark parameter use with its default value."))
            .ToArray();
        var rewriter = new PrototypeRewriter();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var buildStopwatch = Stopwatch.StartNew();
        var plan = rewriter.BuildPlan(root, semanticModel, decisions);
        buildStopwatch.Stop();
        var executeStopwatch = Stopwatch.StartNew();
        var result = rewriter.ExecutePlan(source, "RewritePerformance.cs", plan);
        executeStopwatch.Stop();

        return new RewriteMeasurement(
            Assert.IsType<string>(result.RewrittenSource),
            result.Edits.ToArray(),
            result.Diff.ToString(),
            Assert.IsAssignableFrom<IReadOnlyList<RewritePlanEdit>>(result.Operations).Count,
            buildStopwatch.ElapsedMilliseconds,
            executeStopwatch.ElapsedMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static void AssertCompilable(string source)
    {
        var diagnostics = CreateCompilation(CSharpSyntaxTree.ParseText(source, path: "RewritePerformance.cs"))
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(diagnostics);
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree)
    {
        return CSharpCompilation.Create(
            "RewritePerformanceTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string CreateFixedSource(int referenceCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("namespace Demo;");
        builder.AppendLine("public static class RewritePerformanceFixture");
        builder.AppendLine("{");
        builder.AppendLine("    public static int Run(int value)");
        builder.AppendLine("    {");
        builder.Append("        return ");
        builder.Append(string.Join(" + ", Enumerable.Repeat("value", referenceCount)));
        builder.AppendLine(";");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private sealed record RewriteMeasurement(
        string RewrittenSource,
        IReadOnlyList<RewriteEdit> Edits,
        string Diff,
        int OperationCount,
        long BuildPlanMilliseconds,
        long ExecutePlanMilliseconds,
        long AllocatedBytes);
}
