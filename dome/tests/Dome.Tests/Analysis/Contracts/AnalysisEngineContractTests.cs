using TerrariaTools.Dome.Analysis.Legacy;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using TerrariaTools.Testing.Contracts;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class AnalysisEngineCompatibilityContractTests
{
    [Fact]
    public async Task AnalyzeAsync_ReturnsContextWithStableNativeAbstractions()
    {
        ApplicationAbstractions.IAnalysisEngine engine = new RoslynAnalysisEngine();
        var document = new ApplicationAbstractions.SourceDocument(
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
