using NLISSN.Application;
using NLISSN.Application.Performance;
using NLISSN.Artifacts;
using NLISSN.Infrastructure.Configuration;
using System.Text;
using NLISSN.Core.Analysis;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Rewrite;
using NLISSN.Core.Performance;

namespace NLISSN.Hosting;

/// 读取源目录、调用应用用例，并物化保持顺序的文件输出。
internal sealed class DirectoryAnalysisService
{
    private readonly RulePipeline _pipeline;

    internal DirectoryAnalysisService(RulePipeline pipeline)
    {
        _pipeline = pipeline;
    }

    internal async Task<AnalysisRunOutcome> AnalyzeDirectoryAsync(
      string directoryPath,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime,
      ExecutionSettings execution,
      ArtifactSettings artifacts)
    {
        PerformanceStageScope? directoryScope = runtime.PerformanceStageCollector is not null
          ? PerformanceStageScope.Start(
            PerformanceStageId.DirectoryRead,
            PerformanceStageId.Run,
            directoryPath,
            PerformanceAttributionLevel.Stage)
          : null;
        try
        {
            var filePaths = EnumerateSourceFiles(directoryPath).ToList();
            var sourcesByPath = await ReadSourcesAsync(filePaths, runtime.ExecutionOptions.CancellationToken);
            var outcome = new DirectoryAnalysisUseCase(_pipeline).Analyze(
              filePaths.Select((filePath, index) => new DirectorySourceFile(index, filePath, sourcesByPath[filePath])).ToArray(),
              settings,
              runtime);
            var materialized = MaterializeOutcome(directoryPath, sourcesByPath, outcome, execution, artifacts, runtime);
            if (directoryScope is not null)
            {
                runtime.PerformanceStageCollector!.Record(directoryScope.Complete());
                directoryScope = null;
            }
            return AnalysisRunOutcome.FromDirectory(
              ResolveRunId(artifacts.RunId),
              "directory",
              materialized.Result,
              materialized.Performance,
              inputIdentity: directoryPath);
        }
        catch (Exception exception)
        {
            if (directoryScope is not null)
            {
                runtime.PerformanceStageCollector!.Record(directoryScope.Fail(exception));
            }

            throw;
        }
    }

    internal static DirectoryAnalysisOutcome MaterializeOutcome(
      string directoryPath,
      IReadOnlyDictionary<string, string> sourcesByPath,
      DirectoryAnalysisOutcome outcome,
      ExecutionSettings execution,
      ArtifactSettings artifacts,
      AnalysisRuntime? runtime = null)
    {
        var diffRootPath = artifacts.WriteDiff
          ? artifacts.DiffRoot
          : null;
        var categoryDiffArtifactService = new CategoryDiffArtifactService();
        var writtenDiffCount = 0;

        foreach (var fileResult in outcome.FileResults.OrderBy(result => result.Index))
        {
            if (!fileResult.CanWrite)
            {
                continue;
            }

            var result = fileResult.Result;
            if (result.Edits.Count == 0)
            {
                continue;
            }

            if (diffRootPath is not null)
            {
                var diffScope = runtime?.PerformanceStageCollector is not null
                  ? PerformanceStageScope.Start(
                    PerformanceStageId.ArtifactDiff,
                    PerformanceStageId.Run,
                    fileResult.FilePath,
                    PerformanceAttributionLevel.Stage)
                  : null;
                try
                {
                    writtenDiffCount += categoryDiffArtifactService.Write(
                      directoryPath,
                      fileResult.FilePath,
                      sourcesByPath[fileResult.FilePath],
                      result.Decisions,
                      diffRootPath,
                      artifacts.DiffView);
                    if (diffScope is not null)
                    {
                        runtime!.PerformanceStageCollector!.Record(diffScope.Complete());
                        diffScope = null;
                    }
                }
                catch (Exception exception)
                {
                    if (diffScope is not null)
                    {
                        runtime!.PerformanceStageCollector!.Record(diffScope.Fail(exception));
                    }

                    throw;
                }
            }

            if (execution.WriteBack && result.RewrittenSource is not null)
            {
                var writeBackScope = runtime?.PerformanceStageCollector is not null
                  ? PerformanceStageScope.Start(
                    PerformanceStageId.ArtifactWriteBack,
                    PerformanceStageId.Run,
                    fileResult.FilePath,
                    PerformanceAttributionLevel.Stage)
                  : null;
                try
                {
                    File.WriteAllText(fileResult.FilePath, result.RewrittenSource, Encoding.UTF8);
                    if (writeBackScope is not null)
                    {
                        runtime!.PerformanceStageCollector!.Record(writeBackScope.Complete());
                        writeBackScope = null;
                    }
                }
                catch (Exception exception)
                {
                    if (writeBackScope is not null)
                    {
                        runtime!.PerformanceStageCollector!.Record(writeBackScope.Fail(exception));
                    }

                    throw;
                }
            }
        }

        return outcome with
        {
            Result = outcome.Result with
            {
                DiffFilePath = writtenDiffCount > 0 ? diffRootPath : null,
            }
        };
    }

    private static string ResolveRunId(string runId)
    {
        return string.IsNullOrWhiteSpace(runId) ? "unassigned" : runId;
    }

    private static async Task<Dictionary<string, string>> ReadSourcesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, string>(filePaths.Count, StringComparer.Ordinal);
        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sources[filePath] = await File.ReadAllTextAsync(filePath, cancellationToken);
        }

        return sources;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directoryPath)
    {
        return Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
          .Where(path => !IsIgnoredDirectoryPath(path))
          .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static bool IsIgnoredDirectoryPath(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var segments = path.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
          segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }
}
