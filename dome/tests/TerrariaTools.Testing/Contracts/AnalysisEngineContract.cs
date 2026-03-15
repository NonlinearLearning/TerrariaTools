using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Testing.Contracts;

public static class AnalysisEngineContract
{
    public static async Task AssertStableResultAsync(IAnalysisEngine engine, IReadOnlyList<SourceDocument> documents)
    {
        var result = await engine.AnalyzeAsync(documents, CancellationToken.None);
        var context = result.CreateContext();

        Assert.NotNull(result.View);
        Assert.NotNull(result.Snapshot);
        Assert.NotNull(result.Services);
        Assert.Equal(documents.Count, result.Documents.Count);
        Assert.Equal(result.View, context.View);
        Assert.Same(result.Services.References, context.References);
        Assert.Same(result.Services.Statements, context.Statements);
        Assert.Equal(result.Documents.Count, result.PerformanceSummary.DocumentCount);
    }
}
