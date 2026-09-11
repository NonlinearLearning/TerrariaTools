using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;
using NLISSN.Application;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RewritePerformanceRegressionTests
{
    private const int OperationShapeCount = 8;
    private const int OperationsPerShape = 16;
    private const int ExecutionPathCount = 3;
    private const int ExpectedOperationCount = OperationShapeCount * OperationsPerShape;
    [Fact]
    public void RewritePlan_FixedSource_ProducesEquivalentCompilableResults()
    {
        var source = CreateFixedSource();

        _ = Measure(source);
        var baseline = Measure(source);
        var samples = Enumerable.Range(0, 3).Select(_ => Measure(source)).ToArray();

        Assert.Equal(ExpectedOperationCount, baseline.OperationCount);
        Assert.Equal(OperationShapeCount, baseline.OperationShapeCount);
        Assert.Equal(OperationsPerShape, baseline.MinimumOperationsPerShape);
        AssertEquivalentPaths(baseline);
        Assert.Equal(0, baseline.VerificationFailureCount);
        Assert.True(baseline.VerificationElapsed >= TimeSpan.Zero);
        AssertCompilable(baseline.DirectRewrittenSource);
        AssertCompilable(baseline.PlanRewrittenSource);
        AssertCompilable(baseline.PersistedPlanRewrittenSource);
        foreach (var sample in samples)
        {
            AssertEquivalentPaths(sample);
            Assert.Equal(baseline.DirectRewrittenSource, sample.DirectRewrittenSource);
            Assert.Equal(baseline.Edits, sample.Edits);
            Assert.Equal(baseline.Diff, sample.Diff);
            Assert.Equal(ExpectedOperationCount, sample.OperationCount);
            Assert.Equal(0, sample.VerificationFailureCount);
            AssertCompilable(sample.DirectRewrittenSource);
            AssertCompilable(sample.PlanRewrittenSource);
            AssertCompilable(sample.PersistedPlanRewrittenSource);
        }

    }

    private static RewriteMeasurement Measure(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "RewritePerformance.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var runMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Run");
        var operationShapes = runMethod.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(node => node.Identifier.ValueText == "value")
            .Select(GetOperationShape)
            .GroupBy(kind => kind, StringComparer.Ordinal)
            .ToArray();
        var decisions = runMethod.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(node => node.Identifier.ValueText == "value")
            .Select(node => new RuleDecision(
                node,
                node,
                DecisionActionKind.Delete,
                "Replace benchmark parameter use with its default value."))
            .ToArray();
        var rewriter = new PrototypeRewriter();
        var plan = rewriter.BuildPlan(root, semanticModel, decisions);
        var direct = rewriter.Rewrite(root, semanticModel, decisions);
        var replayed = rewriter.ExecutePlan(source, "RewritePerformance.cs", plan);
        var persistedPlan = JsonSerializer.Deserialize<RewritePlanFile>(
            JsonSerializer.Serialize(new RewritePlanFile("RewritePerformance.cs", "unused", plan.Operations)))!;
        var persisted = rewriter.ExecutePlan(source, "RewritePerformance.cs", persistedPlan);
        var stopwatch = Stopwatch.StartNew();
        var verification = new RewriteVerifier().Verify(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["RewritePerformance.cs"] = source },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RewritePerformance.cs"] = Assert.IsType<string>(direct.RewrittenSource)
            },
            decisions,
            new Dictionary<string, IReadOnlyList<RewritePlanEdit>>(StringComparer.Ordinal)
            {
                ["RewritePerformance.cs"] = plan.Operations
            });
        stopwatch.Stop();

        return new RewriteMeasurement(
            Assert.IsType<string>(direct.RewrittenSource),
            Assert.IsType<string>(replayed.RewrittenSource),
            Assert.IsType<string>(persisted.RewrittenSource),
            replayed.Edits.ToArray(),
            replayed.Diff.ToString(),
            Assert.IsAssignableFrom<IReadOnlyList<RewritePlanEdit>>(replayed.Operations).Count,
            operationShapes.Length,
            operationShapes.Min(group => group.Count()),
            verification.Failures.Count,
            stopwatch.Elapsed);
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

    private static void AssertEquivalentPaths(RewriteMeasurement measurement)
    {
        Assert.Equal(measurement.DirectRewrittenSource, measurement.PlanRewrittenSource);
        Assert.Equal(measurement.DirectRewrittenSource, measurement.PersistedPlanRewrittenSource);
        Assert.Equal(measurement.PlanRewrittenSource, measurement.PersistedPlanRewrittenSource);
    }

    private static string GetOperationShape(IdentifierNameSyntax node)
    {
        return node.Parent switch
        {
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) && ReferenceEquals(binary.Left, node) => "Add:left",
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) => "Add:right",
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.MultiplyExpression) => "Multiply:left",
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.GreaterThanExpression) => "GreaterThan:left",
            ArgumentSyntax argument when argument.Parent?.Parent is InvocationExpressionSyntax => "Invocation:argument",
            ArgumentSyntax argument when argument.Parent?.Parent is ElementAccessExpressionSyntax => "ElementAccess:index",
            CastExpressionSyntax => "Cast:expression",
            ParenthesizedExpressionSyntax => "Parenthesized:expression",
            _ => throw new InvalidOperationException($"Unexpected rewrite operation shape: {node.Parent?.Kind()}.")
        };
    }

    private static string CreateFixedSource()
    {
        var builder = new StringBuilder();
        builder.AppendLine("namespace Demo;");
        builder.AppendLine("public static class RewritePerformanceFixture");
        builder.AppendLine("{");
        builder.AppendLine("    public static int Run(int value)");
        builder.AppendLine("    {");
        builder.AppendLine("        var total = 0;");
        builder.AppendLine("        var values = new int[256];");
        for (var index = 0; index < OperationsPerShape; index++)
        {
            builder.AppendLine($"        total += value + {index};");
            builder.AppendLine($"        total += {index} + value;");
            builder.AppendLine($"        total += value * {index + 1};");
            builder.AppendLine($"        total += value > {index} ? 1 : 0;");
            builder.AppendLine($"        total += Clamp(value, {index});");
            builder.AppendLine("        total += values[value];");
            builder.AppendLine("        total += (int)value;");
            builder.AppendLine("        total += (value);");
        }
        builder.AppendLine("        return total;");
        builder.AppendLine("    }");
        builder.AppendLine("    private static int Clamp(int input, int lower) => input > lower ? input : lower;");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private sealed record RewriteMeasurement(
        string DirectRewrittenSource,
        string PlanRewrittenSource,
        string PersistedPlanRewrittenSource,
        IReadOnlyList<RewriteEdit> Edits,
        string Diff,
        int OperationCount,
        int OperationShapeCount,
        int MinimumOperationsPerShape,
        int VerificationFailureCount,
        TimeSpan VerificationElapsed);
}
