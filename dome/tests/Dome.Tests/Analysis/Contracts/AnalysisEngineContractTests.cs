using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Testing.Contracts;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class AnalysisEngineContractTests
{
    [Fact]
    public async Task AnalyzeAsync_ReturnsContextWithStableCoreAbstractions()
    {
        IAnalysisEngine engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
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

        await AnalysisEngineContract.AssertStableResultAsync(engine, [document]);
    }
}
