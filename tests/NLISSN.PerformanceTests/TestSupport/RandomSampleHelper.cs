using System.Text.Json;
using NLISSN;
using NLISSN.Application;
using NLISSN.Core.Rewrite;

namespace RoslynPrototype.PerformanceTests.TestSupport;

internal enum RandomSampleMode
{
    FixedSeed,
    CustomSeed,
    TrueRandom
}

internal sealed record RandomSampleRequest(
  string SourceDirectory,
  string TargetName,
  RandomSampleMode Mode,
  int SampleCount = 10,
  int? Seed = null,
  string? RunName = null,
  bool WriteBackCopiedSource = true,
  int? MaxDegreeOfParallelism = null);

internal sealed record RandomSampleFileResult(
  string RelativePath,
  string SourcePath,
  string CopiedPath,
  string DiffPath,
  bool Changed);

internal sealed record RandomSampleRunResult(
  RandomSampleMode Mode,
  int? RequestedSeed,
  int EffectiveSeed,
  string WorkingRoot,
  string CopiedSourceRoot,
  string DiffRoot,
  string ManifestPath,
  IReadOnlyList<string> SelectedRelativePaths,
  IReadOnlyList<RandomSampleFileResult> FileResults,
  bool WriteBackApplied,
  PrototypeAnalysisResult AnalysisResult);

internal static class RandomSampleHelper
{
    private const int DefaultFixedSeed = 20260706;

    public static RandomSampleRunResult Execute(RandomSampleRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetName);
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
        var commandHost = new CommandHost(RuleRegistry.CreateDefaultRules());
        var args = new List<string>
        {
            copiedSourceRoot,
            "--delete-class",
            request.TargetName,
            "--fast-delete-class-directory",
            "--diff-out",
            diffRoot
        };
        if (request.WriteBackCopiedSource)
        {
            args.Add("--write-back");
        }

        if (request.MaxDegreeOfParallelism is int maxDegreeOfParallelism)
        {
            args.Add("--max-degree-of-parallelism");
            args.Add(maxDegreeOfParallelism.ToString());
        }

        var analysisResult = commandHost.AnalyzeFromArgs(args.ToArray());
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

        var writeBackApplied = request.WriteBackCopiedSource && analysisResult.Edits.Count > 0;

        return new RandomSampleRunResult(
          request.Mode,
          request.Seed,
          effectiveSeed,
          workingRoot,
          copiedSourceRoot,
          diffRoot,
          manifestPath,
          selectedRelativePaths,
          fileResults,
          writeBackApplied,
          analysisResult);
    }

    private static int ResolveEffectiveSeed(RandomSampleRequest request)
    {
        return request.Mode switch
        {
            RandomSampleMode.FixedSeed => DefaultFixedSeed,
            RandomSampleMode.CustomSeed => request.Seed ??
              throw new InvalidOperationException("CustomSeed mode requires a seed."),
            RandomSampleMode.TrueRandom => Random.Shared.Next(1, int.MaxValue),
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
          "RandomSamples",
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

    private static IReadOnlyList<RandomSampleFileResult> BuildFileResults(string sourceDirectory, string copiedSourceRoot, string diffRoot, IReadOnlyList<string> selectedRelativePaths)
    {
        var results = new List<RandomSampleFileResult>(selectedRelativePaths.Count);
        foreach (var relativePath in selectedRelativePaths)
        {
            var sourcePath = Path.Combine(sourceDirectory, relativePath);
            var copiedPath = Path.Combine(copiedSourceRoot, relativePath);
            var diffPath = Path.Combine(
              diffRoot,
              Path.ChangeExtension(relativePath, ".rewrite.diff"));
            results.Add(new RandomSampleFileResult(
              relativePath,
              sourcePath,
              copiedPath,
              diffPath,
              File.Exists(diffPath)));
        }

        return results;
    }

    private static void WriteManifest(string manifestPath, RandomSampleRequest request, int effectiveSeed, string copiedSourceRoot, string diffRoot, IReadOnlyList<string> selectedRelativePaths, IReadOnlyList<RandomSampleFileResult> fileResults)
    {
        var manifest = new
        {
            mode = request.Mode.ToString(),
            requestedSeed = request.Seed,
            effectiveSeed,
            sampleTarget = request.TargetName,
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
