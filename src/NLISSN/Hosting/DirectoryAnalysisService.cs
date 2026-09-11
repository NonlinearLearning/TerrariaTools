using NLISSN.Application;
using NLISSN.Artifacts;
using NLISSN.Infrastructure.Configuration;
using System.Text;
using NLISSN.Core.Analysis;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Rewrite;

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
        var filePaths = EnumerateSourceFiles(directoryPath).ToList();
        var sourcesByPath = await ReadSourcesAsync(filePaths, runtime.ExecutionOptions.CancellationToken);
        var outcome = new DirectoryAnalysisUseCase(_pipeline).Analyze(
          filePaths.Select((filePath, index) => new DirectorySourceFile(index, filePath, sourcesByPath[filePath])).ToArray(),
          settings,
          runtime);
        var materialized = MaterializeOutcome(directoryPath, sourcesByPath, outcome, execution, artifacts);
        return AnalysisRunOutcome.FromDirectory(
          ResolveRunId(artifacts.RunId),
          "directory",
          materialized.Result,
          materialized.Performance,
          inputIdentity: directoryPath);
    }

    internal static DirectoryAnalysisOutcome MaterializeOutcome(
      string directoryPath,
      IReadOnlyDictionary<string, string> sourcesByPath,
      DirectoryAnalysisOutcome outcome,
      ExecutionSettings execution,
      ArtifactSettings artifacts)
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
                writtenDiffCount += categoryDiffArtifactService.Write(
                  directoryPath,
                  fileResult.FilePath,
                  sourcesByPath[fileResult.FilePath],
                  result.Decisions,
                  diffRootPath,
                  artifacts.DiffView);
            }

            if (execution.WriteBack && result.RewrittenSource is not null)
            {
                File.WriteAllText(fileResult.FilePath, result.RewrittenSource, Encoding.UTF8);
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
