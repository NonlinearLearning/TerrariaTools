using TerrariaTools.Dome.Cli;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Cli;

/// <summary>
/// Dome 命令行解析器测试类。
/// </summary>
public class DomeCliParserTests
{
    /// <summary>
    /// 测试解析异步方法将命令映射到运行模式。
    /// </summary>
    /// <param name="command">命令字符串。</param>
    /// <param name="expectedMode">预期的运行模式。</param>
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

    /// <summary>
    /// 测试解析异步方法加载最小配置文件。
    /// </summary>
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

    /// <summary>
    /// 测试解析异步方法对未知命令失败。
    /// </summary>
    [Fact]
    public async Task ParseAsync_FailsForUnknownCommand()
    {
        var result = await DomeCliParser.ParseAsync(new[] { "noop", "input.cs", "out" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown command", result.ErrorMessage);
    }

    /// <summary>
    /// 测试解析异步方法在配置缺少输入或输出时失败。
    /// </summary>
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
