using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using Xunit;

namespace TerrariaTools.Testing.Contracts;

public static class AnalysisEngineCompatibilityContract
{
    public static async Task AssertStableResultAsync(
        ApplicationAbstractions.IAnalysisEngine engine,
        IReadOnlyList<ModelAnalysis.SourceDocument> documents)
    {
        var sourceSet = new ModelAnalysis.SourceDocumentSet("compatibility-input", "compatibility-root", documents);
        var input = new ModelAnalysis.AnalysisInput(sourceSet, ModelAnalysis.AnalysisInputMode.SourceOnly);
        var result = await engine.AnalyzeAsync(input, CancellationToken.None);
        var context = result.CreateContext();

        Assert.NotNull(result.View);
        Assert.NotNull(result.Snapshot);
        Assert.NotNull(result.Services);
        Assert.NotNull(result.CodePropertyGraph);
        Assert.NotNull(result.Snapshot.CodePropertyGraph);
        Assert.Equal(result.View, context.View);
        Assert.Same(result.CodePropertyGraph, result.Snapshot.CodePropertyGraph);
        Assert.Same(result.CodePropertyGraph, context.CodePropertyGraph);
        Assert.Same(result.Services.References, context.References);
        Assert.Same(result.Services.Statements, context.Statements);
        Assert.Equal(documents.Count, result.PerformanceSummary.DocumentCount);
    }
}



