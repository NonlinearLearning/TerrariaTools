using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class ShadowClosurePlannerTests
{
    [Fact]
    public async Task BuildPlan_IgnoresExternalReachableMethodsWhenGroupingDocuments()
    {
        SourceDocument[] sourceDocuments =
        [
            new SourceDocument(
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
            new SourceDocument(
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

        var loadResult = WorkspaceLoadResult.Success(sourceDocuments, WorkspaceLoadMode.SourceOnly, "StubLoader");
        var request = new TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Terraria.Main.DedServ");
        var input = new ShadowExtractionInputResolution(request, TerrariaRuntimeShadowLayout.Create(request), loadResult);
        var analysisResult = await new RoslynAnalysisEngine().AnalyzeAsync(loadResult.AnalysisInput!, CancellationToken.None);
        var context = analysisResult.CreateContext();
        var seedNode = context.FunctionIndex.NodesByMemberId["Terraria.Main.DedServ()"];
        var analysis = new ShadowExtractionAnalysis(input, analysisResult, context, seedNode);

        var result = new ShadowClosurePlanner().BuildPlan(analysis, new FakeTerrariaRuntimeProgressReporter(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Helper.cs", "Main.cs"], result.Value!.IncludedDocuments);
    }
}
