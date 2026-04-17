using Analysis.Semantic.AccessPath;
using Xunit;

namespace Analysis.Tests.Semantic;

public sealed class AccessPathTests
{
    [Fact]
    public void Append_returnsNullWhenExtensionIsExcluded()
    {
        AccessPath path = new AccessPath(new AccessElement[] { new ConstantAccess("user") })
            .AddExclusion(new ConstantAccess("password"));

        AccessPath? appended = path.Append(new ConstantAccess("password"));

        Assert.Null(appended);
    }

    [Fact]
    public void Append_returnsNewPathWhenExtensionIsAllowed()
    {
        AccessPath path = new AccessPath(new AccessElement[] { new ConstantAccess("user") });

        AccessPath? appended = path.Append(new ConstantAccess("profile"));

        Assert.NotNull(appended);
        Assert.Equal("user", appended!.Elements[0].ToString());
        Assert.Equal("profile", appended.Elements[1].ToString());
    }

    [Fact]
    public void TrackedBase_toStringUsesStableReadableFormat()
    {
        Assert.Equal("TrackedReturnValue(foo())", new TrackedReturnValue("foo()").ToString());
        Assert.Equal("TrackedLiteral(1)", new TrackedLiteral("1").ToString());
        Assert.Equal("TrackedUnknown", TrackedUnknown.Instance.ToString());
    }
}
