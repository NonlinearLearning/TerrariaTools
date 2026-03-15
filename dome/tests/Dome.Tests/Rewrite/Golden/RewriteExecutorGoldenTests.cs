using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rewrite.Roslyn;
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

        var context = CreateRewriteContext("Sample.cs", source);
        var root = (CompilationUnitSyntax)context.Root;
        var classNode = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>());
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToDictionary(method => method.Identifier.ValueText, StringComparer.Ordinal);
        var deleteStatement = Assert.Single(methods["Helper"].DescendantNodes().OfType<LocalDeclarationStatementSyntax>());
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.Helper()"),
                        MemberKind.Method,
                        TargetKind.Method,
                        methods["Helper"].SpanStart,
                        methods["Helper"].Span.Length,
                        methods["Helper"].ToString().Trim(),
                        new TargetResolutionKey(methods["Helper"].SpanStart, methods["Helper"].Span.Length)),
                    new PlanAction(PlanActionKind.ChangeVisibilityToPrivate),
                    new PlanReason("member-cleanup", "privatize helper")),
                new PlannedChange(
                    1,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player"),
                        MemberKind.Class,
                        TargetKind.Class,
                        classNode.SpanStart,
                        classNode.Span.Length,
                        classNode.Identifier.ValueText,
                        new TargetResolutionKey(classNode.SpanStart, classNode.Span.Length)),
                    new PlanAction(PlanActionKind.ReorderPublicMethods),
                    new PlanReason("member-cleanup", "reorder public methods")),
                new PlannedChange(
                    2,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.Helper()"),
                        MemberKind.Method,
                        TargetKind.Statement,
                        deleteStatement.SpanStart,
                        deleteStatement.Span.Length,
                        deleteStatement.ToString().Trim(),
                        new TargetResolutionKey(deleteStatement.SpanStart, deleteStatement.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("statement-delete", "delete temp"))
            },
            Array.Empty<PlanConflict>());

        var result = await new RoslynRewriteExecutor().ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RewrittenSource);
        await VerifySettingsFixture.VerifyText(result.RewrittenSource!);
    }

    private static RewriteExecutionDocumentContext CreateRewriteContext(string relativePath, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: relativePath);
        var root = tree.GetCompilationUnitRoot();
        var compilation = CSharpCompilation.Create(
            "RewriteExecutorGoldenTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        return new RewriteExecutionDocumentContext(
            new SourceDocument(relativePath, relativePath, source),
            root,
            compilation.GetSemanticModel(tree));
    }
}
