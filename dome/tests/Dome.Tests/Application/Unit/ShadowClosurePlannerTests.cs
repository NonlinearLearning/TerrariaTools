using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using PortsCommon = TerrariaTools.Dome.Application.Ports;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class ShadowClosurePlannerLegacyTests
{
    [Fact]
    public async Task BuildPlan_IgnoresExternalReachableMethodsWhenGroupingDocuments()
    {
        ModelAnalysis.SourceDocument[] sourceDocuments =
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
            new ModelAnalysis.AnalysisInput(
                new ModelAnalysis.SourceDocumentSet("input.sln", Directory.GetCurrentDirectory(), sourceDocuments),
                ModelAnalysis.AnalysisInputMode.SourceOnly),
            PortsCommon.WorkspaceLoadMode.SourceOnly,
            "StubLoader");
        var request = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Terraria.Main.DedServ");
        var input = new ShadowExtractionInputResolution(request, new TerrariaRuntimeShadowLayoutFactory().Create(request), loadResult);
        var analysisResult = await new RoslynAnalysisEngine().AnalyzeAsync(loadResult.Input!, CancellationToken.None);
        var analysis = new ShadowExtractionAnalysis(input, analysisResult);

        var result = new ShadowClosurePlanner(new SeedClosureAnalyzer())
            .BuildPlan(analysis, new FakeTerrariaRuntimeCompatibilityProgressReporter(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Helper.cs", "Main.cs"], result.Value!.IncludedDocuments);
        Assert.Equal("Terraria.Main.DedServ()", result.Value.SeedNode.MemberId.Value);
    }
}



