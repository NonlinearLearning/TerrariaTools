using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using TerrariaTools.Testing.Contracts;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class AnalysisEngineCompatibilityContractTests
{
    [Fact]
    public async Task AnalyzeAsync_ReturnsContextWithStableNativeAbstractions()
    {
        ApplicationAbstractions.IAnalysisEngine engine = new RoslynAnalysisEngine();
        var document = new ModelAnalysis.SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Worker
            {
                public void Run()
                {
                }
            }
            """);

        await AnalysisEngineCompatibilityContract.AssertStableResultAsync(engine, [document]);
    }
}
