using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using TerrariaTools.Testing.GoldenOutputs;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rewrite;

public sealed class RewriteExecutorGoldenTests
{
    [Fact]
    public async Task ExecuteAsync_ProducesStableOutputForDeleteVisibilityAndReorder()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Zebra()
                {
                }

                public void Helper()
                {
                    int temp = 1;
                }

                public void Alpha()
                {
                }

                private void Hidden()
                {
                }
            }
            """;

        var root = CSharpSyntaxTree.ParseText(source, path: "Sample.cs").GetCompilationUnitRoot();
        var classNode = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>());
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToDictionary(method => method.Identifier.ValueText, StringComparer.Ordinal);
        var deleteStatement = Assert.Single(methods["Helper"].DescendantNodes().OfType<LocalDeclarationStatementSyntax>());
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "input.cs", "out", ModelPrimitives.RunMode.Standard),
            new[]
            {
                new ModelPlanning.PlannedChange(
                    0,
                    new ModelPrimitives.TargetIdentity(
                        "Sample.cs",
                        new ModelPrimitives.MemberId("Sample.Player.Helper()"),
                        ModelPrimitives.MemberKind.Method,
                        ModelPrimitives.TargetKind.Method),
                    new ModelPrimitives.TargetLocator(
                        methods["Helper"].SpanStart,
                        methods["Helper"].Span.Length,
                        methods["Helper"].ToString().Trim(),
                        new ModelPrimitives.TargetResolutionKey(methods["Helper"].SpanStart, methods["Helper"].Span.Length)),
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.ChangeVisibilityToPrivate),
                    new ModelPlanning.PlanReason("member-cleanup", "privatize helper")),
                new ModelPlanning.PlannedChange(
                    1,
                    new ModelPrimitives.TargetIdentity(
                        "Sample.cs",
                        new ModelPrimitives.MemberId("Sample.Player"),
                        ModelPrimitives.MemberKind.Class,
                        ModelPrimitives.TargetKind.Class),
                    new ModelPrimitives.TargetLocator(
                        classNode.SpanStart,
                        classNode.Span.Length,
                        classNode.Identifier.ValueText,
                        new ModelPrimitives.TargetResolutionKey(classNode.SpanStart, classNode.Span.Length)),
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.ReorderPublicMethods),
                    new ModelPlanning.PlanReason("member-cleanup", "reorder public methods")),
                new ModelPlanning.PlannedChange(
                    2,
                    new ModelPrimitives.TargetIdentity(
                        "Sample.cs",
                        new ModelPrimitives.MemberId("Sample.Player.Helper()"),
                        ModelPrimitives.MemberKind.Method,
                        ModelPrimitives.TargetKind.Statement),
                    new ModelPrimitives.TargetLocator(
                        deleteStatement.SpanStart,
                        deleteStatement.Span.Length,
                        deleteStatement.ToString().Trim(),
                        new ModelPrimitives.TargetResolutionKey(deleteStatement.SpanStart, deleteStatement.Span.Length)),
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                    new ModelPlanning.PlanReason("statement-delete", "delete temp"))
            },
            Array.Empty<ModelPlanning.PlanConflict>());
        var sourceSet = new ModelAnalysis.SourceDocumentSet(
            "Sample.cs",
            "Sample.cs",
            [new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", source)]);

        var result = await new RoslynRewriteExecutor().ExecuteAsync(sourceSet, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RewrittenSource);
        await VerifyCompatibilitySettingsFixture.VerifyText(result.RewrittenSource!);
    }
}

