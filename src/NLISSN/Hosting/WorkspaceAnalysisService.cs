using NLISSN.Application;
using NLISSN.Application.Performance;
using NLISSN.Artifacts;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Rewrite;
using NLISSN.Core.Performance;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Infrastructure.Workspace;
using System.Diagnostics;

namespace NLISSN.Hosting;

/// Loads MSBuild projects and runs the existing Application analysis per project.
internal sealed class WorkspaceAnalysisService
{
    private readonly RulePipeline _pipeline;
    private readonly MsBuildWorkspaceInputLoader _loader = new();

    /// <summary>
    /// 本次分析实际使用的工作区快照；加载成功前为 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 供同进程内的后续步骤（项目级 JSON 导出）复用，以免为同一工程再跑一次 MSBuild 加载。
    /// 只暴露给宿主内部消费；调用方必须先自行确认「复用是等价的」（见导出器的重载说明）。
    /// </remarks>
    internal WorkspaceSolutionSnapshot? LoadedSnapshot { get; private set; }

    internal WorkspaceAnalysisService(RulePipeline pipeline)
    {
        _pipeline = pipeline;
    }

    internal async Task<AnalysisRunOutcome> AnalyzeAsync(
      WorkspaceInputOptions options,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime,
      ExecutionSettings execution,
      ArtifactSettings artifacts)
    {
        PerformanceStageScope? loadScope = runtime.PerformanceStageCollector is not null
          ? PerformanceStageScope.Start(
            PerformanceStageId.WorkspaceLoad,
            PerformanceStageId.Run,
            options.Path,
            PerformanceAttributionLevel.Stage)
          : null;
        try
        {
            var loadResult = await _loader.LoadAsync(
              options,
              runtime.ExecutionOptions.CancellationToken).ConfigureAwait(false);
            if (!loadResult.IsSuccess)
            {
                throw new InvalidOperationException(FormatDiagnostics(loadResult.Diagnostics));
            }
            PerformanceStageSample? workspaceLoadStage = null;
            if (loadScope is not null)
            {
                workspaceLoadStage = loadScope.Complete();
                runtime.PerformanceStageCollector!.Record(workspaceLoadStage);
                loadScope = null;
            }

            var stopwatch = Stopwatch.StartNew();
            LoadedSnapshot = loadResult.Snapshot;
            var projectOutcomes = new List<DirectoryAnalysisOutcome>(loadResult.Snapshot!.Projects.Count);
            var projectPerformance = new List<WorkspaceProjectPerformanceFacts>(loadResult.Snapshot.Projects.Count);
            var useCase = new DirectoryAnalysisUseCase(_pipeline);
            foreach (var project in loadResult.Snapshot.Projects)
            {
                runtime.ExecutionOptions.CancellationToken.ThrowIfCancellationRequested();
                var projectId = WorkspacePerformanceFactAggregator.CreateProjectId(
                  project.ProjectPath,
                  project.TargetFramework);
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
                  runtime,
                  projectId);
                var sourcesByPath = sources.ToDictionary(
                  source => source.FilePath,
                  source => source.Source,
                  StringComparer.Ordinal);
                var projectDirectory = Path.GetDirectoryName(project.ProjectPath)
                  ?? loadResult.Snapshot.SolutionDirectory;
                var materialized = DirectoryAnalysisService.MaterializeOutcome(
                  projectDirectory,
                  sourcesByPath,
                  outcome,
                  execution,
                  artifacts,
                  runtime);
                projectOutcomes.Add(materialized);
                projectPerformance.Add(new WorkspaceProjectPerformanceFacts(
                  projectId,
                  project.ProjectPath,
                  project.ProjectName,
                  project.TargetFramework,
                  materialized.Performance,
                  materialized.Performance?.Status ?? PerformanceStatus.Unavailable,
                  materialized.Performance?.ErrorKind));
            }

            stopwatch.Stop();
            var combinedResult = DirectoryAnalysisUseCase.CombineResults(
              projectOutcomes.Select(outcome => outcome.Result).ToArray(),
              options.TargetDocumentPath is null
                ? loadResult.Snapshot.IsSolution ? "workspace" : "project"
                : "document");
            var performance = DirectoryPerformanceFactAggregator.Aggregate(
              "workspace",
              projectOutcomes.SelectMany(outcome => outcome.FileResults).ToArray(),
              stopwatch.ElapsedMilliseconds);
            var workspace = WorkspacePerformanceFactAggregator.Aggregate(
              options.Path,
              projectPerformance,
              workspaceLoadStage,
              performance.Status,
              performance.ErrorKind);
            return AnalysisRunOutcome.FromWorkspace(
              string.IsNullOrWhiteSpace(artifacts.RunId) ? "unassigned" : artifacts.RunId,
              options.TargetDocumentPath is null
                ? loadResult.Snapshot.IsSolution ? "workspace" : "project"
                : "document",
              combinedResult,
              performance,
              workspace,
              inputIdentity: options.Path);
        }
        catch (Exception exception)
        {
            if (loadScope is not null)
            {
                runtime.PerformanceStageCollector!.Record(loadScope.Fail(exception));
            }

            throw;
        }
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
