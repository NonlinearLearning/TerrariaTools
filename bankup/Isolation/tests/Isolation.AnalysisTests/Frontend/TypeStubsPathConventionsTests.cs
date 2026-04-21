using Logic.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class TypeStubsPathConventionsTests
{
    [Fact]
    public void NormalizePath_returnsAbsolutePath()
    {
        string path = TypeStubsPathConventions.NormalizePath(".\\docs");

        Assert.True(Path.IsPathFullyQualified(path));
    }

    [Fact]
    public void CreateConfig_returnsEmptyConfigForBlankInput()
    {
        TypeStubsParserConfig config = TypeStubsPathConventions.CreateConfig(null);

        Assert.Null(config.TypeStubsFilePath);
    }
}
