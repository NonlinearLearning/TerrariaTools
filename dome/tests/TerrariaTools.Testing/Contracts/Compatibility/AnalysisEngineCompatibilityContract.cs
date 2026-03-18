using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using Xunit;

namespace TerrariaTools.Testing.Contracts;

/// <summary>
/// Compatibility-only assertions for native analysis contracts exposed through the legacy namespace.
/// </summary>
public static class AnalysisEngineCompatibilityContract
{
    public static async Task AssertStableResultAsync(ApplicationAbstractions.IAnalysisEngine engine, IReadOnlyList<ApplicationAbstractions.SourceDocument> documents)
    {
        var sourceSet = new ApplicationAbstractions.SourceDocumentSet("compatibility-input", "compatibility-root", documents);
        var result = await engine.AnalyzeAsync(sourceSet, CancellationToken.None);
        var context = result.CreateContext();

        Assert.NotNull(result.View);
        Assert.NotNull(result.Snapshot);
        Assert.NotNull(result.Services);
        Assert.Equal(result.View, context.View);
        Assert.Same(result.Services.References, context.References);
        Assert.Same(result.Services.Statements, context.Statements);
        Assert.Equal(documents.Count, result.PerformanceSummary.DocumentCount);
    }
}
