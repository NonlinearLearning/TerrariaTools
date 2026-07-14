using RoslynPrototype.Tests.TestCodeSet.Common;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DeleteClassRandomSampleHelperTests : IDisposable
{
    private readonly string _sourceDirectory;
    private const string DeleteClassTargetName = "PlayerInput";
    private const int TerrariaTimingReferenceFileLimit = 99;
    private const int TerrariaTimingMaxDegreeOfParallelism = 64;
    private const string TerrariaExternalCodeSetPath =
      @"D:\lodes\TR\Backup\New1.27\1.45 2\TR";

    public DeleteClassRandomSampleHelperTests()
    {
        _sourceDirectory = Path.Combine(
          Path.GetTempPath(),
          $"delete-class-random-sample-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sourceDirectory);
        CreateSourceFiles(_sourceDirectory, 15);
    }

    [Fact]
    public void Execute_FixedSeedMode_SelectsStableFiles()
    {
        var first = DeleteClassRandomSampleHelper.Execute(new DeleteClassRandomSampleRequest(
          _sourceDirectory,
          "PlayerInput",
          DeleteClassRandomSampleMode.FixedSeed));
        var second = DeleteClassRandomSampleHelper.Execute(new DeleteClassRandomSampleRequest(
          _sourceDirectory,
          "PlayerInput",
          DeleteClassRandomSampleMode.FixedSeed));

        Assert.Equal(first.EffectiveSeed, second.EffectiveSeed);
        Assert.Equal(first.SelectedRelativePaths, second.SelectedRelativePaths);
        Assert.Equal(10, first.SelectedRelativePaths.Count);
    }

    [Fact]
    public void Execute_CustomSeedMode_UsesRequestedSeedAndCanSwitchSamples()
    {
        var first = DeleteClassRandomSampleHelper.Execute(new DeleteClassRandomSampleRequest(
          _sourceDirectory,
          "PlayerInput",
          DeleteClassRandomSampleMode.CustomSeed,
          Seed: 7));
        var second = DeleteClassRandomSampleHelper.Execute(new DeleteClassRandomSampleRequest(
          _sourceDirectory,
          "PlayerInput",
          DeleteClassRandomSampleMode.CustomSeed,
          Seed: 7));
        var third = DeleteClassRandomSampleHelper.Execute(new DeleteClassRandomSampleRequest(
          _sourceDirectory,
          "PlayerInput",
          DeleteClassRandomSampleMode.CustomSeed,
          Seed: 99));

        Assert.Equal(7, first.EffectiveSeed);
        Assert.Equal(first.SelectedRelativePaths, second.SelectedRelativePaths);
        Assert.NotEqual(first.SelectedRelativePaths, third.SelectedRelativePaths);
    }

    [Fact]
    public void Execute_TrueRandomMode_WritesManifestWithEffectiveSeed()
    {
        var result = DeleteClassRandomSampleHelper.Execute(new DeleteClassRandomSampleRequest(
          _sourceDirectory,
          "PlayerInput",
          DeleteClassRandomSampleMode.TrueRandom));

        Assert.True(result.EffectiveSeed > 0);
        Assert.True(File.Exists(result.ManifestPath));
        TextDiffAssert.Contains("\"mode\": \"TrueRandom\"", File.ReadAllText(result.ManifestPath));
        TextDiffAssert.Contains("\"effectiveSeed\":", File.ReadAllText(result.ManifestPath));
    }

    [Fact]
    public void Execute_EndToEnd_CopiesFilesAndProducesDiffArtifacts()
    {
        var result = DeleteClassRandomSampleHelper.Execute(new DeleteClassRandomSampleRequest(
          _sourceDirectory,
          "PlayerInput",
          DeleteClassRandomSampleMode.FixedSeed));

        Assert.Equal(10, result.FileResults.Count);
        Assert.All(result.FileResults, file =>
        {
            Assert.True(File.Exists(file.CopiedPath));
            Assert.True(File.Exists(file.DiffPath));
        });
        Assert.True(Directory.Exists(result.CopiedSourceRoot));
        Assert.True(Directory.Exists(result.DiffRoot));
        Assert.NotEmpty(result.AnalysisResult.DiffText);
    }

    [Fact]
    public void Execute_TerrariaCodeSet_ReportsPhaseTimingsWithoutApplyingFinalWriteBack()
    {
        var stagedSourceDirectory = CreateTargetedTerrariaSourceDirectory();
        var sampleCount = CountCandidateFiles(stagedSourceDirectory);
        Assert.True(sampleCount > 0, $"Missing target sources under {TerrariaExternalCodeSetPath}");

        var result = DeleteClassRandomSampleHelper.Execute(new DeleteClassRandomSampleRequest(
          stagedSourceDirectory,
          DeleteClassTargetName,
          DeleteClassRandomSampleMode.FixedSeed,
          SampleCount: sampleCount,
          WriteBackCopiedSource: false,
          MaxDegreeOfParallelism: TerrariaTimingMaxDegreeOfParallelism));
        var phaseTimings = Assert.IsType<DeleteClassRandomSampleAnalysisPhaseTimings>(
          result.Timings.AnalysisPhases);

        Console.WriteLine(
          $"[terraria-phase-timing] sourceRoot={TerrariaExternalCodeSetPath};" +
          $"stagedRoot={stagedSourceDirectory};" +
          $"files={result.SelectedRelativePaths.Count};" +
          $"maxDegreeOfParallelism={TerrariaTimingMaxDegreeOfParallelism};" +
          $"writeBackApplied={result.WriteBackApplied};" +
          $"copyMs={result.Timings.CopyMilliseconds};" +
          $"analysisMs={result.Timings.AnalysisMilliseconds};" +
          $"prepMs={phaseTimings.PreparationMilliseconds};" +
          $"cpgMs={phaseTimings.CpgBuildMilliseconds};" +
          $"markMs={phaseTimings.MarkMilliseconds};" +
          $"propagateMs={phaseTimings.PropagateMilliseconds};" +
          $"liftMs={phaseTimings.LiftMilliseconds};" +
          $"decideMs={phaseTimings.DecideMilliseconds};" +
          $"rewriteMs={phaseTimings.RewriteMilliseconds};" +
          $"phaseTotalMs={phaseTimings.TotalMilliseconds};" +
          $"diffMs={result.Timings.DiffMaterializationMilliseconds};" +
          $"manifestMs={result.Timings.ManifestMilliseconds};" +
          $"writeBackMs={result.Timings.WriteBackMilliseconds};" +
          $"totalMs={result.Timings.TotalMilliseconds}");

        Assert.Equal(sampleCount, result.SelectedRelativePaths.Count);
        Assert.False(result.WriteBackApplied);
        Assert.NotEmpty(result.AnalysisResult.DiffText);
        Assert.True(result.AnalysisResult.Edits.Count > 0);
        Assert.True(result.Timings.CopyMilliseconds >= 0);
        Assert.True(result.Timings.AnalysisMilliseconds >= 0);
        Assert.True(result.Timings.DiffMaterializationMilliseconds >= 0);
        Assert.True(result.Timings.ManifestMilliseconds >= 0);
        Assert.Equal(0, result.Timings.WriteBackMilliseconds);
        Assert.True(phaseTimings.MarkMilliseconds >= 0);
        Assert.True(phaseTimings.PropagateMilliseconds >= 0);
        Assert.True(phaseTimings.LiftMilliseconds >= 0);
        Assert.True(phaseTimings.DecideMilliseconds >= 0);
        Assert.True(phaseTimings.RewriteMilliseconds >= 0);
        Assert.All(result.FileResults, file =>
        {
            Assert.True(File.Exists(file.CopiedPath));
            Assert.Equal(File.ReadAllText(file.SourcePath), File.ReadAllText(file.CopiedPath));
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceDirectory))
        {
            Directory.Delete(_sourceDirectory, recursive: true);
        }
    }

    private static void CreateSourceFiles(string sourceDirectory, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var directory = Path.Combine(sourceDirectory, $"Group{index % 3:00}");
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, $"Sample{index:00}.cs");
            File.WriteAllText(filePath, $$"""
              namespace Demo.Group{{index}};

              public sealed class PlayerInput
              {
              }

              public sealed class Controller{{index}}
              {
                private PlayerInput _cached = new PlayerInput();

                public PlayerInput Current => _cached;
              }
              """);
        }
    }

    private static int CountCandidateFiles(string sourceDirectory)
    {
        return Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
          .Count(path => !IsIgnoredCodeSetPath(path, sourceDirectory));
    }

    private static bool IsIgnoredCodeSetPath(string path, string rootDirectory)
    {
        var relativePath = Path.GetRelativePath(rootDirectory, path);
        return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
          .Any(part =>
            string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private string CreateTargetedTerrariaSourceDirectory()
    {
        Assert.True(
          Directory.Exists(TerrariaExternalCodeSetPath),
          $"Missing external code set: {TerrariaExternalCodeSetPath}");
        var stagedSourceDirectory = Path.Combine(_sourceDirectory, "terraria-targeted-source");
        Directory.CreateDirectory(stagedSourceDirectory);
        var targetDefinitionPath = Path.Combine(
          TerrariaExternalCodeSetPath,
          "Terraria.GameInput",
          "PlayerInput.cs");
        Assert.True(File.Exists(targetDefinitionPath), $"Missing target definition: {targetDefinitionPath}");

        var sourcePaths = new[] { targetDefinitionPath }
          .Concat(Directory.EnumerateFiles(
            TerrariaExternalCodeSetPath,
            "*.cs",
            SearchOption.AllDirectories)
            .Where(path => !IsIgnoredCodeSetPath(path, TerrariaExternalCodeSetPath))
            .Where(path => !string.Equals(path, targetDefinitionPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(DeleteClassTargetName, StringComparison.Ordinal))
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.Length)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(TerrariaTimingReferenceFileLimit)
            .Select(file => file.FullName));

        foreach (var sourcePath in sourcePaths)
        {
            var relativePath = Path.GetRelativePath(TerrariaExternalCodeSetPath, sourcePath);
            var stagedPath = Path.Combine(stagedSourceDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            File.Copy(sourcePath, stagedPath, overwrite: true);
        }

        return stagedSourceDirectory;
    }
}
