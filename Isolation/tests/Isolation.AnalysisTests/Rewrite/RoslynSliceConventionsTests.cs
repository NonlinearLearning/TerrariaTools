using Logic.Rewrite;
using Xunit;

namespace Isolation.AnalysisTests.Rewrite;

public sealed class RoslynSliceConventionsTests
{
    [Fact]
    public void BuildFieldMemberName_joinsVariableNames()
    {
        string name = RoslynSliceConventions.BuildFieldMemberName(new[] { "x", "y" });

        Assert.Equal("x,y", name);
    }

    [Fact]
    public void BuildFallbackMemberName_returnsStableValue()
    {
        Assert.Equal("EventDeclaration", RoslynSliceConventions.BuildFallbackMemberName("EventDeclaration"));
        Assert.Equal("<unknown>", RoslynSliceConventions.BuildFallbackMemberName(null));
    }
}
