using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

namespace TerrariaTools.Dome.Analysis.Legacy;

/// <summary>
/// Compatibility wrapper interface that now exposes only native workspace load contracts.
/// </summary>
public interface IWorkspaceLoader
{
    Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(
        string inputPath,
        ApplicationAbstractions.WorkspaceLoadOptions options,
        CancellationToken cancellationToken);
}

public sealed class CodeAnalysisWorkspaceLoader : IWorkspaceLoader
{
    private readonly TerrariaTools.Dome.Analysis.Roslyn.CodeAnalysisWorkspaceLoader _inner = new();

    public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(
        string inputPath,
        ApplicationAbstractions.WorkspaceLoadOptions options,
        CancellationToken cancellationToken) =>
        _inner.LoadAsync(inputPath, options, cancellationToken);
}

public sealed class SourceOnlyLoader : IWorkspaceLoader
{
    private readonly ApplicationAbstractions.IWorkspaceLoader _inner = new TerrariaTools.Dome.Analysis.Roslyn.SourceOnlyLoader();

    public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(
        string inputPath,
        ApplicationAbstractions.WorkspaceLoadOptions options,
        CancellationToken cancellationToken) =>
        _inner.LoadAsync(inputPath, options, cancellationToken);
}

public sealed class WorkspaceLoadCoordinator : IWorkspaceLoader
{
    private readonly ApplicationAbstractions.IWorkspaceLoader _inner;

    public WorkspaceLoadCoordinator(IWorkspaceLoader codeAnalysisLoader, IWorkspaceLoader sourceOnlyLoader)
    {
        _inner = new TerrariaTools.Dome.Analysis.Roslyn.WorkspaceLoadCoordinator(
            new LoaderAdapter(codeAnalysisLoader),
            new LoaderAdapter(sourceOnlyLoader));
    }

    public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(
        string inputPath,
        ApplicationAbstractions.WorkspaceLoadOptions options,
        CancellationToken cancellationToken) =>
        _inner.LoadAsync(inputPath, options, cancellationToken);

    private sealed class LoaderAdapter(IWorkspaceLoader inner) : ApplicationAbstractions.IWorkspaceLoader
    {
        public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(
            string inputPath,
            ApplicationAbstractions.WorkspaceLoadOptions options,
            CancellationToken cancellationToken) =>
            inner.LoadAsync(inputPath, options, cancellationToken);
    }
}
