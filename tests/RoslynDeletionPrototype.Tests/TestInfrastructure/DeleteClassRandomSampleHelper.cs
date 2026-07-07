using System.Text.Json;
using RoslynPrototype.Application;
using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Tests;

internal enum DeleteClassRandomSampleMode
{
    FixedSeed,
    CustomSeed,
    TrueRandom
}

internal sealed record DeleteClassRandomSampleRequest(
  string SourceDirectory,
  string DeleteClassTarget,
  DeleteClassRandomSampleMode Mode,
  int SampleCount = 10,
  int? Seed = null,
  string? RunName = null);

internal sealed record DeleteClassRandomSampleFileResult(
  string RelativePath,
  string SourcePath,
  string CopiedPath,
  string DiffPath,
  bool Changed);

internal sealed record DeleteClassRandomSampleRunResult(
  DeleteClassRandomSampleMode Mode,
  int? RequestedSeed,
  int EffectiveSeed,
  string WorkingRoot,
  string CopiedSourceRoot,
  string DiffRoot,
  string ManifestPath,
  IReadOnlyList<string> SelectedRelativePaths,
  IReadOnlyList<DeleteClassRandomSampleFileResult> FileResults,
  PrototypeAnalysisResult AnalysisResult);

internal static class DeleteClassRandomSampleHelper
{
    private const int DefaultFixedSeed = 20260706;

    public static DeleteClassRandomSampleRunResult Execute(DeleteClassRandomSampleRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DeleteClassTarget);
        if (!Directory.Exists(request.SourceDirectory))
        {
            throw new DirectoryNotFoundException(request.SourceDirectory);
        }

        if (request.SampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SampleCount));
        }

        var candidateRelativePaths = EnumerateCandidateRelativePaths(request.SourceDirectory);
        if (candidateRelativePaths.Count < request.SampleCount)
        {
            throw new InvalidOperationException(
              $"Source directory only contains {candidateRelativePaths.Count} candidate files, " +
              $"but {request.SampleCount} were requested.");
        }

        var effectiveSeed = ResolveEffectiveSeed(request);
        var selectedRelativePaths = SelectRelativePaths(
          candidateRelativePaths,
          request.SampleCount,
          effectiveSeed);
        var workingRoot = CreateWorkingRoot(request.RunName);
        var copiedSourceRoot = Path.Combine(workingRoot, "source");
        var diffRoot = Path.Combine(workingRoot, "diffs");
        Directory.CreateDirectory(copiedSourceRoot);
        Directory.CreateDirectory(diffRoot);

        foreach (var relativePath in selectedRelativePaths)
        {
            var sourcePath = Path.Combine(request.SourceDirectory, relativePath);
            var copiedPath = Path.Combine(copiedSourceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(copiedPath)!);
            File.Copy(sourcePath, copiedPath, overwrite: true);
        }

        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
        var analysisResult = application.AnalyzeFromArgs(
        [
            copiedSourceRoot,
            "--delete-class",
            request.DeleteClassTarget,
            "--fast-delete-class-directory",
            "--write-back",
            "--diff-out",
            diffRoot
        ]);
        var fileResults = BuildFileResults(
          request.SourceDirectory,
          copiedSourceRoot,
          diffRoot,
          selectedRelativePaths);
        var manifestPath = Path.Combine(workingRoot, "manifest.json");
        WriteManifest(
          manifestPath,
          request,
          effectiveSeed,
          copiedSourceRoot,
          diffRoot,
          selectedRelativePaths,
          fileResults);

        return new DeleteClassRandomSampleRunResult(
          request.Mode,
          request.Seed,
          effectiveSeed,
          workingRoot,
          copiedSourceRoot,
          diffRoot,
          manifestPath,
          selectedRelativePaths,
          fileResults,
          analysisResult);
    }

    private static int ResolveEffectiveSeed(DeleteClassRandomSampleRequest request)
    {
        return request.Mode switch
        {
            DeleteClassRandomSampleMode.FixedSeed => DefaultFixedSeed,
            DeleteClassRandomSampleMode.CustomSeed => request.Seed ??
              throw new InvalidOperationException("CustomSeed mode requires a seed."),
            DeleteClassRandomSampleMode.TrueRandom => Random.Shared.Next(1, int.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
    }

    private static List<string> EnumerateCandidateRelativePaths(string sourceDirectory)
    {
        return Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
          .Where(path => !IsIgnoredDirectoryPath(path))
          .Select(path => Path.GetRelativePath(sourceDirectory, path))
          .OrderBy(path => path, StringComparer.Ordinal)
          .ToList();
    }

    private static bool IsIgnoredDirectoryPath(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var segments = path.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
          segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SelectRelativePaths(IReadOnlyList<string> candidates, int sampleCount, int seed)
    {
        var pool = candidates.ToList();
        var random = new Random(seed);
        for (var index = pool.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (pool[index], pool[swapIndex]) = (pool[swapIndex], pool[index]);
        }

        return pool
          .Take(sampleCount)
          .OrderBy(path => path, StringComparer.Ordinal)
          .ToList();
    }

    private static string CreateWorkingRoot(string? runName)
    {
        var root = Path.Combine(
          ResolveRepositoryRoot(),
          "Build",
          "DeleteClassRandomSamples",
          string.IsNullOrWhiteSpace(runName)
            ? $"run-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"
            : SanitizeRunName(runName));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string SanitizeRunName(string runName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new char[runName.Length];
        for (var index = 0; index < runName.Length; index++)
        {
            builder[index] = invalidCharacters.Contains(runName[index]) ? '_' : runName[index];
        }

        return new string(builder);
    }

    private static IReadOnlyList<DeleteClassRandomSampleFileResult> BuildFileResults(string sourceDirectory, string copiedSourceRoot, string diffRoot, IReadOnlyList<string> selectedRelativePaths)
    {
        var results = new List<DeleteClassRandomSampleFileResult>(selectedRelativePaths.Count);
        foreach (var relativePath in selectedRelativePaths)
        {
            var sourcePath = Path.Combine(sourceDirectory, relativePath);
            var copiedPath = Path.Combine(copiedSourceRoot, relativePath);
            var diffPath = Path.Combine(
              diffRoot,
              Path.ChangeExtension(relativePath, ".rewrite.diff"));
            results.Add(new DeleteClassRandomSampleFileResult(
              relativePath,
              sourcePath,
              copiedPath,
              diffPath,
              File.Exists(diffPath)));
        }

        return results;
    }

    private static void WriteManifest(string manifestPath, DeleteClassRandomSampleRequest request, int effectiveSeed, string copiedSourceRoot, string diffRoot, IReadOnlyList<string> selectedRelativePaths, IReadOnlyList<DeleteClassRandomSampleFileResult> fileResults)
    {
        var manifest = new
        {
            mode = request.Mode.ToString(),
            requestedSeed = request.Seed,
            effectiveSeed,
            deleteClassTarget = request.DeleteClassTarget,
            sampleCount = request.SampleCount,
            sourceDirectory = Path.GetFullPath(request.SourceDirectory),
            copiedSourceRoot,
            diffRoot,
            selectedRelativePaths,
            fileResults
        };
        var json = JsonSerializer.Serialize(
          manifest,
          new JsonSerializerOptions
          {
              WriteIndented = true
          });
        File.WriteAllText(manifestPath, json);
    }

    private static string ResolveRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
          AppContext.BaseDirectory,
          "..",
          "..",
          "..",
          ".."));
    }
}
