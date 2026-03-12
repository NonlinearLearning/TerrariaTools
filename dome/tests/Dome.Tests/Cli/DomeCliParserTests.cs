using TerrariaTools.Dome.Cli;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Cli;

public class DomeCliParserTests
{
    [Theory]
    [InlineData("run", RunMode.Standard)]
    [InlineData("analyze", RunMode.AnalyzeOnly)]
    [InlineData("plan", RunMode.PlanOnly)]
    public async Task ParseAsync_MapsCommandsToRunModes(string command, RunMode expectedMode)
    {
        var result = await DomeCliParser.ParseAsync(new[] { command, "input.cs", "out" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal(expectedMode, result.Request!.Mode);
    }

    [Fact]
    public async Task ParseAsync_LoadsMinimalConfigFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var configPath = Path.Combine(tempRoot, "dome.config.json");
            await File.WriteAllTextAsync(
                configPath,
                """
                {
                  "Command": "plan",
                  "InputPath": "input.cs",
                  "OutputPath": "out",
                  "RuleSet": ["r1"],
                  "LogLevel": "Info"
                }
                """);

            var result = await DomeCliParser.ParseAsync(new[] { "--config", configPath }, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Request);
            Assert.Equal(RunMode.PlanOnly, result.Request!.Mode);
            Assert.Single(result.Request.RuleSet);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ParseAsync_FailsForUnknownCommand()
    {
        var result = await DomeCliParser.ParseAsync(new[] { "noop", "input.cs", "out" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown command", result.ErrorMessage);
    }

    [Fact]
    public async Task ParseAsync_FailsWhenConfigMissesInputOrOutput()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var configPath = Path.Combine(tempRoot, "dome.config.json");
            await File.WriteAllTextAsync(
                configPath,
                """
                {
                  "Command": "run",
                  "InputPath": "input.cs"
                }
                """);

            var result = await DomeCliParser.ParseAsync(new[] { "--config", configPath }, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains("InputPath and OutputPath", result.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
