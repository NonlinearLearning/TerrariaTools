using Deletion.Application;
using System.Diagnostics;
using System.Text;
using Deletion.Cli.Telemetry;
using Deletion.Core.Analysis;
using Deletion.Core.Rewrite;
using Deletion.Rules;

namespace Deletion.Cli;

internal sealed class DeletionDirectoryAnalysisService
{
    private readonly DeletionRulePipeline _pipeline;

    internal DeletionDirectoryAnalysisService(DeletionRulePipeline pipeline)
    {
        _pipeline = pipeline;
    }

    internal PrototypeAnalysisResult AnalyzeDirectory(
      string directoryPath,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime)
    {
        return AnalyzeDirectoryAsync(directoryPath, options, runtime).GetAwaiter().GetResult();
    }

    internal async Task<PrototypeAnalysisResult> AnalyzeDirectoryAsync(
      string directoryPath,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime,
      AnalysisTextLogWriter? analysisWriter = null)
    {
        var filePaths = EnumerateSourceFiles(directoryPath).ToList();
        var sourcesByPath = await ReadSourcesAsync(filePaths, runtime.ExecutionOptions.CancellationToken);
        var directoryStopwatch = Stopwatch.StartNew();
        var outcome = new DirectoryAnalysisUseCase(_pipeline).Analyze(
          filePaths.Select((filePath, index) => new DirectorySourceFile(index, filePath, sourcesByPath[filePath])).ToArray(),
          options,
          runtime);
        directoryStopwatch.Stop();
        return MaterializeOutcome(
          directoryPath,
          options,
          runtime,
          analysisWriter,
          outcome,
          directoryStopwatch.ElapsedMilliseconds);
    }

    private static PrototypeAnalysisResult MaterializeOutcome(
      string directoryPath,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime,
      AnalysisTextLogWriter? analysisWriter,
      DirectoryAnalysisOutcome outcome,
      long directoryAnalysisMilliseconds)
    {
        var shouldWriteDiff = DeletionApplicationOptions.ShouldWriteDiff(options);
        var shouldWriteBack = DeletionApplicationOptions.ShouldWriteBack(options);
        var diffRootPath = shouldWriteDiff
          ? DeletionDiffPathResolver.ResolveDirectoryDiffRoot(directoryPath, options)
          : null;
        var renderer = new TextDiffRenderer();
        var diffWriteStopwatch = Stopwatch.StartNew();
        var writtenDiffCount = 0;
        var deferredDiffResults = DeletionApplicationOptions.ShouldUseDeleteClassUsingCleanup(options)
          ? outcome.FileResults.Where(result => result.Result.Edits.Count > 0).OrderBy(result => result.Index).ToArray()
          : Array.Empty<DirectoryFileAnalysisResult>();

        foreach (var fileResult in deferredDiffResults)
        {
            analysisWriter?.WriteDiffPending(fileResult.FilePath, fileResult.Result.Edits.Count);
        }

        if (deferredDiffResults.Length > 0)
        {
            analysisWriter?.WriteDiffCleanupStarted(deferredDiffResults.Length);
            analysisWriter?.WriteDiffCleanupCompleted(deferredDiffResults.Length, 0);
        }

        foreach (var fileResult in outcome.FileResults.OrderBy(result => result.Index))
        {
            var result = fileResult.Result;
            analysisWriter?.WriteResult(fileResult.FilePath, result);
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
                var diffPath = DeletionDiffPathResolver.ResolveFileDiffPath(
                  directoryPath,
                  fileResult.FilePath,
                  diffRootPath);
                Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
                File.WriteAllText(
                  diffPath,
                  renderer.Render(result.Diff.Files.Single(), DeletionApplicationOptions.ResolveDiffView(options)),
                  Encoding.UTF8);
                writtenDiffCount++;
                analysisWriter?.WriteDiffWritten(fileResult.FilePath, diffPath, result.Edits.Count);
            }
        }

        diffWriteStopwatch.Stop();
        analysisWriter?.WriteDirectoryPublicationSummary(
          outcome.PublicationTelemetry.FileCount,
          outcome.PublicationTelemetry.UnpublishedCountPeak,
          outcome.PublicationTelemetry.WaitToPublishMilliseconds,
          outcome.PublicationTelemetry.OldestUnpublishedIndex);
        if (deferredDiffResults.Length > 0)
        {
            analysisWriter?.WriteDiffSummary(
              deferredDiffResults.Length,
              writtenDiffCount,
              diffWriteStopwatch.ElapsedMilliseconds,
              diffWriteStopwatch.ElapsedMilliseconds);
        }

        return outcome.Result with
        {
          DiffFilePath = writtenDiffCount > 0 ? diffRootPath : null,
          Stats = outcome.Result.Stats! with
          {
            DirectoryAnalysisMilliseconds = directoryAnalysisMilliseconds,
          },
        };
    }

    private static async Task<Dictionary<string, string>> ReadSourcesAsync(
      IReadOnlyList<string> filePaths,
      CancellationToken cancellationToken)
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
