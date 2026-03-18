using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Analysis.Legacy;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class ShadowClosurePlannerLegacyTests
{
    [Fact]
    public async Task BuildPlan_IgnoresExternalReachableMethodsWhenGroupingDocuments()
    {
        ApplicationAbstractions.SourceDocument[] sourceDocuments =
        [
            new(
                "Main.cs",
                "Main.cs",
                """
                using System;

                namespace Terraria;

                public static class Main
                {
                    public static void DedServ()
                    {
                        Helper.Run(static () => { });
                    }
                }
                """),
            new(
                "Helper.cs",
                "Helper.cs",
                """
                using System;

                namespace Terraria;

                internal static class Helper
                {
                    public static void Run(Action action)
                    {
                        action();
                    }
                }
                """)
        ];

        var loadResult = ApplicationAbstractions.WorkspaceLoadResult.Success(
            new ApplicationAbstractions.SourceDocumentSet("input.sln", Directory.GetCurrentDirectory(), sourceDocuments),
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            "StubLoader");
        var request = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Terraria.Main.DedServ");
        var input = new ShadowExtractionInputResolution(request, ApplicationAbstractions.TerrariaRuntimeShadowLayout.Create(request), loadResult);
        var analysisResult = await new RoslynAnalysisEngine().AnalyzeAsync(loadResult.SourceSet!, CancellationToken.None);
        var context = analysisResult.CreateContext();
        var seedNode = context.FunctionIndex.NodesByMemberId["Terraria.Main.DedServ()"];
        var documents = BuildDocuments(sourceDocuments);
        var analysis = new ShadowExtractionAnalysis(input, analysisResult, context, seedNode, documents);

        var result = new ShadowClosurePlanner().BuildPlan(analysis, new FakeTerrariaRuntimeCompatibilityProgressReporter(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Helper.cs", "Main.cs"], result.Value!.IncludedDocuments);
    }

    private static IReadOnlyList<ShadowExtractionAnalysisDocument> BuildDocuments(IReadOnlyList<ApplicationAbstractions.SourceDocument> sourceDocuments)
    {
        var trees = sourceDocuments.Select(document => CSharpSyntaxTree.ParseText(document.SourceText, path: document.SourcePath)).ToArray();
        var compilation = CSharpCompilation.Create(
            "ShadowClosureTests",
            trees,
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Action).Assembly.Location)
            ]);

        return sourceDocuments.Select(document =>
        {
            var tree = compilation.SyntaxTrees.Single(candidate => string.Equals(candidate.FilePath, document.SourcePath, StringComparison.OrdinalIgnoreCase));
            return new ShadowExtractionAnalysisDocument(document, (CompilationUnitSyntax)tree.GetRoot(), compilation.GetSemanticModel(tree));
        }).ToArray();
    }
}
