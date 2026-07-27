using NLISSN.Application;
using System.Text;
using NLISSN.Core.Analysis;
using NLISSN.Core.Rewrite;
using NLISSN.Rules;

namespace NLISSN;

/// 读取源目录、调用应用用例，并物化保持顺序的文件输出。
internal sealed class DirectoryAnalysisService
{
    private readonly RulePipeline _pipeline;

    internal DirectoryAnalysisService(RulePipeline pipeline)
    {
        _pipeline = pipeline;
    }

    internal PrototypeAnalysisResult AnalyzeDirectory(string directoryPath, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime)
    {
        return AnalyzeDirectoryAsync(directoryPath, options, runtime).GetAwaiter().GetResult();
    }

    internal async Task<PrototypeAnalysisResult> AnalyzeDirectoryAsync(string directoryPath, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime)
    {
        var filePaths = EnumerateSourceFiles(directoryPath).ToList();
        var sourcesByPath = await ReadSourcesAsync(filePaths, runtime.ExecutionOptions.CancellationToken);
        var outcome = new DirectoryAnalysisUseCase(_pipeline).Analyze(
          filePaths.Select((filePath, index) => new DirectorySourceFile(index, filePath, sourcesByPath[filePath])).ToArray(),
          options,
          runtime);
        return MaterializeOutcome(directoryPath, options, outcome);
    }

    private static PrototypeAnalysisResult MaterializeOutcome(string directoryPath, IReadOnlyDictionary<string, string> options, DirectoryAnalysisOutcome outcome)
    {
        var shouldWriteDiff = ApplicationOptions.ShouldWriteDiff(options);
        var shouldWriteBack = ApplicationOptions.ShouldWriteBack(options);
        var diffRootPath = shouldWriteDiff
          ? DiffPathResolver.ResolveDirectoryDiffRoot(directoryPath, options)
          : null;
        var renderer = new TextDiffRenderer();
        var writtenDiffCount = 0;

        foreach (var fileResult in outcome.FileResults.OrderBy(result => result.Index))
        {
            var result = fileResult.Result;
            if (result.Edits.Count == 0)
            {
                continue;
            }

            if (shouldWriteBack && result.RewrittenSource is not null)
            {
                File.WriteAllText(fileResult.FilePath, result.RewrittenSource, Encoding.UTF8);
            }

            if (diffRootPath is not null && result.Diff.Files.Count > 0)
            {
                var diffPath = DiffPathResolver.ResolveFileDiffPath(
                  directoryPath,
                  fileResult.FilePath,
                  diffRootPath);
                Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
                File.WriteAllText(
                  diffPath,
                  renderer.Render(result.Diff.Files.Single(), ApplicationOptions.ResolveDiffView(options)),
                  Encoding.UTF8);
                writtenDiffCount++;
            }
        }

        return outcome.Result with
        {
            DiffFilePath = writtenDiffCount > 0 ? diffRootPath : null,
        };
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
