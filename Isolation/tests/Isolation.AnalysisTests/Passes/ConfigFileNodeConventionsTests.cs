using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class ConfigFileNodeConventionsTests
{
    [Fact]
    public void ApplyConfigFileProperties_writesConfigShape()
    {
        CpgNode node = new(1, CpgNodeKind.ConfigFile);

        ConfigFileNodeConventions.ApplyConfigFileProperties(node, "appsettings.json", "{ }", 9);

        Assert.True(node.TryGetProperty<string>("Name", out string? name));
        Assert.True(node.TryGetProperty<string>("Content", out string? content));
        Assert.Equal("appsettings.json", name);
        Assert.Equal("{ }", content);
    }
}
