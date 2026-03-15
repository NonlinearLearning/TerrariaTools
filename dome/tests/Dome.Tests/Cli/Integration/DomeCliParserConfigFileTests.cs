using TerrariaTools.Dome.Cli;
using TerrariaTools.Dome.Core;
using TerrariaTools.Testing.TestBuilders;
using TerrariaTools.Testing.TestFixtures;
using Xunit;

namespace TerrariaTools.Dome.Tests.Cli;

public sealed class DomeCliParserConfigFileTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _directories;

    public DomeCliParserConfigFileTests(TemporaryDirectoryFixture directories)
    {
        _directories = directories;
    }

    [Fact]
    public async Task ParseAsync_LoadsConfigurationFromFilePath()
    {
        var configPath = _directories.GetPath("dome.config.json");
        await File.WriteAllTextAsync(
            configPath,
            new ConfigJsonBuilder()
                .WithCommand("plan")
                .WithInputPath("input.csproj")
                .WithOutputPath("out")
                .WithLoader("sourceonly")
                .WithAllowFallback(false)
                .Build());

        var result = await DomeCliParser.ParseAsync(new[] { "--config", configPath }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal(WorkspaceLoaderPreference.SourceOnly, result.Request!.WorkspaceLoadOptions.PreferredLoader);
        Assert.False(result.Request.WorkspaceLoadOptions.AllowFallbackToSourceOnly);
    }

    [Fact]
    public async Task ParseAsync_FailsWhenConfigPathDoesNotExist()
    {
        var configPath = _directories.GetPath("missing.config.json");

        var result = await DomeCliParser.ParseAsync(new[] { "--config", configPath }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("was not found", result.ErrorMessage);
    }
}
