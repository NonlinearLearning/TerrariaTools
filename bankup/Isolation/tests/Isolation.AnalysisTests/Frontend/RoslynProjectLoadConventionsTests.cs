using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class RoslynProjectLoadConventionsTests
{
    [Fact]
    public void InputTypeDetectors_matchExpectedExtensions()
    {
        Assert.True(RoslynProjectLoadConventions.IsSolutionFile("demo.sln"));
        Assert.True(RoslynProjectLoadConventions.IsProjectFile("demo.csproj"));
        Assert.True(RoslynProjectLoadConventions.IsSourceFile("demo.cs"));
        Assert.False(RoslynProjectLoadConventions.IsSourceFile("demo.txt"));
    }

    [Fact]
    public void ErrorMessages_areStable()
    {
        Assert.Contains("不支持的输入文件类型", RoslynProjectLoadConventions.BuildUnsupportedFileTypeMessage("a.txt"));
        Assert.Contains("输入路径不存在", RoslynProjectLoadConventions.BuildMissingInputPathMessage("missing"));
        Assert.Contains("没有找到任何 C# 源文件", RoslynProjectLoadConventions.BuildNoSourceFilesMessage("src"));
    }
}
