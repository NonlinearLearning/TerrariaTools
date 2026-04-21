using Logic.Analysis.Engine.X2Cpg;
using Xunit;

namespace Isolation.AnalysisTests.X2Cpg;

public sealed class SourceFileDiscoveryRulesTests
{
    [Fact]
    public void IsFileSizeAllowed_respectsMaxFileSize()
    {
        Assert.True(SourceFileDiscoveryRules.IsFileSizeAllowed(10, 10));
        Assert.False(SourceFileDiscoveryRules.IsFileSizeAllowed(11, 10));
    }

    [Fact]
    public void HasSupportedExtension_matchesCaseInsensitiveSuffix()
    {
        Assert.True(SourceFileDiscoveryRules.HasSupportedExtension("Demo.CS", new[] { ".cs" }));
        Assert.False(SourceFileDiscoveryRules.HasSupportedExtension("Demo.txt", new[] { ".cs" }));
    }

    [Fact]
    public void OrderFiles_usesOrdinalOrder()
    {
        IReadOnlyList<string> files = SourceFileDiscoveryRules.OrderFiles(new[] { "b.cs", "a.cs" });

        Assert.Equal(new[] { "a.cs", "b.cs" }, files);
    }
}
