using Logic.Analysis.Engine.Frontend;
using Logic.Analysis.Engine.Passes;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class ImportDirectiveConventionsTests
{
    [Fact]
    public void CreateImportDirectiveInfo_derivesAliasAndCoordinates()
    {
        ImportDirectiveInfo info = ImportDirectiveConventions.CreateImportDirectiveInfo(
            "demo.cs",
            "System.Collections.Generic.List",
            null,
            "using System.Collections.Generic.List;",
            2,
            10,
            4,
            isStatic: false,
            isGlobal: true);

        Assert.Equal("List", info.ImportedAs);
        Assert.Equal(2, info.Order);
        Assert.Equal(10, info.Line);
        Assert.True(info.IsGlobal);
    }
}
