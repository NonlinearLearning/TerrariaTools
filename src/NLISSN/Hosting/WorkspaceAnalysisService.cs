using NLISSN.Application;
using NLISSN.Artifacts;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Rewrite;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Infrastructure.Workspace;

namespace NLISSN.Hosting;

/// Loads MSBuild projects and runs the existing Application analysis per project.
internal sealed class WorkspaceAnalysisService
{
    private readonly RulePipeline _pipeline;
    private readonly MsBuildWorkspaceInputLoader _loader = new();

    internal WorkspaceAnalysisService(RulePipeline pipeline)
    {
        _pipeline = pipeline;
    }

    internal async Task<PrototypeAnalysisResult> AnalyzeAsync(
      WorkspaceInputOptions options,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime,
      ExecutionSettings execution,
      ArtifactSettings artifacts)
    {
        var loadResult = await _loader.LoadAsync(
          options,
          runtime.ExecutionOptions.CancellationToken).ConfigureAwait(false);
        if (!loadResult.IsSuccess)
        {
            throw new InvalidOperationException(FormatDiagnostics(loadResult.Diagnostics));
        }

        var projectResults = new List<PrototypeAnalysisResult>(loadResult.Snapshot!.Projects.Count);
        var useCase = new DirectoryAnalysisUseCase(_pipeline);
        foreach (var project in loadResult.Snapshot.Projects)
        {
            runtime.ExecutionOptions.CancellationToken.ThrowIfCancellationRequested();
            var documents = string.IsNullOrWhiteSpace(options.TargetDocumentPath)
              ? project.Documents
              : project.Documents
                .Where(document => PathsEqual(document.FilePath, options.TargetDocumentPath))
                .ToArray();
            var sources = documents
              .Select((document, index) => new CompiledDirectorySourceFile(
                index,
                document.FilePath,
                document.Source,
                document.SyntaxTree,
                document.IsGenerated,
                document.CanWrite))
              .ToArray();
            var outcome = useCase.AnalyzeCompiled(
              project.Compilation,
              sources,
              settings,
              runtime);
            var sourcesByPath = sources.ToDictionary(
              source => source.FilePath,
              source => source.Source,
              StringComparer.Ordinal);
            var projectDirectory = Path.GetDirectoryName(project.ProjectPath)
              ?? loadResult.Snapshot.SolutionDirectory;
            projectResults.Add(DirectoryAnalysisService.MaterializeOutcome(
              projectDirectory,
              sourcesByPath,
              outcome,
              execution,
              artifacts));
        }

        return DirectoryAnalysisUseCase.CombineResults(
          projectResults,
          options.TargetDocumentPath is null
            ? loadResult.Snapshot.IsSolution ? "workspace" : "project"
            : "document");
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
          Path.GetFullPath(left),
          Path.GetFullPath(right),
          StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDiagnostics(IReadOnlyList<WorkspaceInputDiagnostic> diagnostics)
    {
        var lines = diagnostics
          .OrderBy(diagnostic => diagnostic.ProjectPath, StringComparer.OrdinalIgnoreCase)
          .ThenBy(diagnostic => diagnostic.Path, StringComparer.OrdinalIgnoreCase)
          .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
          .Select(diagnostic =>
          {
              var location = diagnostic.ProjectPath ?? diagnostic.Path ?? "workspace";
              var targetFramework = diagnostic.TargetFramework is null
                ? string.Empty
                : $" [{diagnostic.TargetFramework}]";
              return $"{diagnostic.Code} {diagnostic.Severity} {location}{targetFramework}: {diagnostic.Message}";
          });
        return "Workspace input could not be analyzed:\n" + string.Join("\n", lines);
    }
}
