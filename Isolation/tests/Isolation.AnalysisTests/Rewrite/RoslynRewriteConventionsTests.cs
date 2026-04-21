using Logic.Rewrite;
using Xunit;

namespace Isolation.AnalysisTests.Rewrite;

public sealed class RoslynRewriteConventionsTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldUseEmptyStatement_returnsExpectedValue(bool input, bool expected)
    {
        Assert.Equal(expected, RoslynRewriteConventions.ShouldUseEmptyStatement(input));
    }

    [Theory]
    [InlineData("string", true)]
    [InlineData(" String ", false)]
    [InlineData("int", false)]
    public void ShouldUseNullLiteral_matchesOnlyStringKeyword(string keyword, bool expected)
    {
        Assert.Equal(expected, RoslynRewriteConventions.ShouldUseNullLiteral(keyword));
    }

    [Fact]
    public void IsClassLocalDependency_requiresExactContainingTypeMatch()
    {
        Assert.True(RoslynRewriteConventions.IsClassLocalDependency("Demo", "Demo"));
        Assert.False(RoslynRewriteConventions.IsClassLocalDependency("Demo.Inner", "Demo"));
        Assert.False(RoslynRewriteConventions.IsClassLocalDependency(null, "Demo"));
    }

    [Fact]
    public void ShouldRewriteIdentifier_requiresExactIdentifierMatch()
    {
        Assert.True(RoslynRewriteConventions.ShouldRewriteIdentifier("Demo", "Demo"));
        Assert.False(RoslynRewriteConventions.ShouldRewriteIdentifier("DemoShadow", "Demo"));
        Assert.False(RoslynRewriteConventions.ShouldRewriteIdentifier(null, "Demo"));
    }
}
