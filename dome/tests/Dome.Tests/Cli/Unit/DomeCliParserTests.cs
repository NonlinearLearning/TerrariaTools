using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Cli;
using TerrariaTools.Testing.TestBuilders;
using Xunit;

namespace TerrariaTools.Dome.Tests.Cli;

public class DomeCliParserTests
{
    [Theory]
    [InlineData("run", ModelPrimitives.RunMode.Standard)]
    [InlineData("analyze", ModelPrimitives.RunMode.AnalyzeOnly)]
    [InlineData("plan", ModelPrimitives.RunMode.PlanOnly)]
    public async Task ParseAsync_MapsCommandsToRunModes(string command, ModelPrimitives.RunMode expectedMode)
    {
        var result = await DomeCliParser.ParseAsync(new[] { command, "input.cs", "out" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal(expectedMode, result.Request!.Mode);
        Assert.Equal(ApplicationAbstractions.WorkspaceLoaderPreference.Auto, result.Request.WorkspaceLoadOptions.PreferredLoader);
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
        Assert.Equal(ApplicationAbstractions.WorkspaceLoaderPreference.CodeAnalysisFirst, result.Request!.WorkspaceLoadOptions.PreferredLoader);
        Assert.False(result.Request.WorkspaceLoadOptions.AllowFallbackToSourceOnly);
    }

    [Fact]
    public async Task ParseAsync_RejectsLegacyTrRunCommand()
    {
        var result = await DomeCliParser.ParseAsync(new[] { "tr-run" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Request);
        Assert.Contains("Legacy runtime commands", result.ErrorMessage);
    }

    [Fact]
    public async Task ParseAsync_RejectsLegacyTrShadowCommand()
    {
        var result = await DomeCliParser.ParseAsync(new[] { "tr-shadow" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Request);
        Assert.Contains("Legacy runtime commands", result.ErrorMessage);
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
        Assert.Equal(ModelPrimitives.RunMode.PlanOnly, result.Request!.Mode);
        Assert.Single(result.Request.RuleSet);
        Assert.Equal(ApplicationAbstractions.WorkspaceLoaderPreference.SourceOnly, result.Request.WorkspaceLoadOptions.PreferredLoader);
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
