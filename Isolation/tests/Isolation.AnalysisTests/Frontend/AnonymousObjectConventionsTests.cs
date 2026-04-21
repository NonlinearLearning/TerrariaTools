using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class AnonymousObjectConventionsTests
{
    [Fact]
    public void BuildMemberName_prefersExplicitNameThenIdentifierThenMemberAccess()
    {
        Assert.Equal("Alias", AnonymousObjectConventions.BuildMemberName("Alias", "id", "member", "expr"));
        Assert.Equal("id", AnonymousObjectConventions.BuildMemberName(null, "id", "member", "expr"));
        Assert.Equal("member", AnonymousObjectConventions.BuildMemberName(null, null, "member", "expr"));
        Assert.Equal("expr", AnonymousObjectConventions.BuildMemberName(null, null, null, "expr"));
    }

    [Fact]
    public void AnonymousObjectMemberSource_isStable()
    {
        Assert.Equal("AnonymousObjectMember", AnonymousObjectConventions.AnonymousObjectMemberSource);
    }
}
