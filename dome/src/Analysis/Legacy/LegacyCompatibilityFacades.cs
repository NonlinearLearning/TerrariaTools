using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

namespace TerrariaTools.Dome.Analysis.Legacy;

/// <summary>
/// Compatibility-only factory surface that keeps legacy orchestration decoupled from concrete native implementations.
/// </summary>
public sealed class LegacyAnalysisEngineFacade(RoslynAnalysisEngine innerEngine) : ApplicationAbstractions.IAnalysisEngine
{
    public Task<ApplicationAbstractions.AnalysisEngineResult> AnalyzeAsync(
        ApplicationAbstractions.SourceDocumentSet sourceSet,
        CancellationToken cancellationToken) =>
        innerEngine.AnalyzeAsync(sourceSet, cancellationToken);
}

/// <summary>
/// Compatibility-only factory surface for workspace loading on native contracts.
/// </summary>
public sealed class LegacyWorkspaceLoaderFacade(WorkspaceLoadCoordinator innerLoader) : IWorkspaceLoader
{
    public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(
        string inputPath,
        ApplicationAbstractions.WorkspaceLoadOptions options,
        CancellationToken cancellationToken) =>
        innerLoader.LoadAsync(inputPath, options, cancellationToken);
}
