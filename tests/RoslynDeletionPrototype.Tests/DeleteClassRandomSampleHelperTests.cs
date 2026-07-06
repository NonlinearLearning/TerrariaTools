using RoslynPrototype.Tests.TestCodeSet.Common;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DeleteClassRandomSampleHelperTests : IDisposable
{
    private readonly string _sourceDirectory;

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
}
