using Logic.Analysis.Engine.Passes;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class ConfigFileConventionsTests
{
    [Fact]
    public void IsConfigFile_matchesKnownNamesAndExtensions()
    {
        Assert.True(ConfigFileConventions.IsConfigFile("appsettings.json"));
        Assert.True(ConfigFileConventions.IsConfigFile("demo.targets"));
        Assert.True(ConfigFileConventions.IsConfigFile("nuget.config"));
        Assert.False(ConfigFileConventions.IsConfigFile("readme.md"));
    }

    [Fact]
    public void ResolveRootPath_handlesFileAndEmptyInput()
    {
        Assert.Equal(string.Empty, ConfigFileConventions.ResolveRootPath(null));
        Assert.Equal(string.Empty, ConfigFileConventions.ResolveRootPath(string.Empty));
    }
}
