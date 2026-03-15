using TerrariaTools.Dome.Cli;
using TerrariaTools.Dome.Core;
using TerrariaTools.Testing.TestBuilders;
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
        Assert.Equal(WorkspaceLoaderPreference.Auto, result.Request.WorkspaceLoadOptions.PreferredLoader);
        Assert.True(result.Request.WorkspaceLoadOptions.AllowFallbackToSourceOnly);
    }

    [Fact]
    public async Task ParseAsync_MapsLoaderArgumentsToWorkspaceLoadOptions()
    {
        var result = await DomeCliParser.ParseAsync(
            new[] { "run", "input.cs", "out", "--loader", "codeanalysis", "--no-fallback" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal(WorkspaceLoaderPreference.CodeAnalysisFirst, result.Request!.WorkspaceLoadOptions.PreferredLoader);
        Assert.False(result.Request.WorkspaceLoadOptions.AllowFallbackToSourceOnly);
    }

    [Fact]
    public async Task ParseAsync_ParsesTrRunCommand()
    {
        var result = await DomeCliParser.ParseAsync(new[] { "tr-run" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Request);
        Assert.NotNull(result.TerrariaRuntimeRunRequest);
        Assert.Equal(
            @"D:\lodes\TR\Backup\New1.27\1.45\TR\TerrariaServer.sln",
            result.TerrariaRuntimeRunRequest!.SolutionPath);
        Assert.Equal(
            @"D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-runtime",
            result.TerrariaRuntimeRunRequest.OutputRootPath);
    }

    [Fact]
    public async Task ParseAsync_ParsesTrShadowCommand()
    {
        var result = await DomeCliParser.ParseAsync(new[] { "tr-shadow" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Request);
        Assert.NotNull(result.TerrariaRuntimeShadowExtractionRequest);
        Assert.Equal(
            @"D:\lodes\TR\Backup\New1.27\1.45\TR\TerrariaServer.sln",
            result.TerrariaRuntimeShadowExtractionRequest!.SolutionPath);
        Assert.Equal(
            @"D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-shadow",
            result.TerrariaRuntimeShadowExtractionRequest.OutputRootPath);
        Assert.Equal("Terraria.Main.DedServ", result.TerrariaRuntimeShadowExtractionRequest.SeedMemberName);
    }

    [Fact]
    public void ParseConfigJson_LoadsMinimalConfiguration()
    {
        var json = new ConfigJsonBuilder()
            .WithCommand("plan")
            .WithInputPath("input.cs")
            .WithOutputPath("out")
            .WithRuleSet("r1")
            .WithLogLevel("Info")
            .WithLoader("sourceonly")
            .WithAllowFallback(false)
            .Build();

        var result = DomeCliParser.ParseConfigJson(json);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal(RunMode.PlanOnly, result.Request!.Mode);
        Assert.Single(result.Request.RuleSet);
        Assert.Equal(WorkspaceLoaderPreference.SourceOnly, result.Request.WorkspaceLoadOptions.PreferredLoader);
        Assert.False(result.Request.WorkspaceLoadOptions.AllowFallbackToSourceOnly);
    }

    [Fact]
    public void ParseConfigJson_FailsWhenConfigMissesInputOrOutput()
    {
        var json = new ConfigJsonBuilder()
            .WithCommand("run")
            .WithInputPath("input.cs")
            .WithOutputPath(null)
            .Build();

        var result = DomeCliParser.ParseConfigJson(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("InputPath and OutputPath", result.ErrorMessage);
    }

    [Fact]
    public void ParseConfigJson_FailsForUnknownLoader()
    {
        var json = new ConfigJsonBuilder()
            .WithLoader("mystery")
            .Build();

        var result = DomeCliParser.ParseConfigJson(json);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown config loader", result.ErrorMessage);
    }

    [Fact]
    public void ParseConfigJson_FailsForInvalidJson()
    {
        var result = DomeCliParser.ParseConfigJson("{ invalid json");

        Assert.False(result.IsSuccess);
        Assert.Contains("invalid JSON", result.ErrorMessage);
    }

    [Fact]
    public async Task ParseAsync_FailsForUnknownCommand()
    {
        var result = await DomeCliParser.ParseAsync(new[] { "noop", "input.cs", "out" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown command", result.ErrorMessage);
    }
}
