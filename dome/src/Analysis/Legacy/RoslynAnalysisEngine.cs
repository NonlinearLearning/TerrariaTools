using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;

namespace TerrariaTools.Dome.Analysis.Legacy;

/// <summary>
/// Compatibility wrapper that preserves the legacy namespace while delegating to the standard native Roslyn engine.
/// </summary>
public sealed class RoslynAnalysisEngine : ApplicationAbstractions.IAnalysisEngine
{
    private readonly TerrariaTools.Dome.Analysis.Roslyn.RoslynAnalysisEngine _inner = new();

    public Task<ApplicationAbstractions.AnalysisEngineResult> AnalyzeAsync(
        ApplicationAbstractions.SourceDocumentSet sourceSet,
        CancellationToken cancellationToken) =>
        _inner.AnalyzeAsync(sourceSet, cancellationToken);

    public ModelAnalysis.AnalysisExecutionSnapshot CreateSnapshot(ApplicationAbstractions.AnalysisEngineResult result) => result.Snapshot;

    public ModelAnalysis.AnalysisServices CreateServices(ApplicationAbstractions.AnalysisEngineResult result) => result.Services;

    public ModelAnalysis.AnalysisContext CreateContext(ApplicationAbstractions.AnalysisEngineResult result) => result.CreateContext();
}
