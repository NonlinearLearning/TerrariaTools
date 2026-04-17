using Analysis.Frontend;
using Xunit;

namespace Analysis.Tests.Frontend;

public sealed class TypeStubsParserTests
{
    [Fact]
    public void CreateConfig_normalizesAbsolutePath()
    {
        string relativePath = Path.Combine(".", "types.txt");

        TypeStubsParserConfig config = TypeStubsParser.CreateConfig(relativePath);

        Assert.NotNull(config.TypeStubsFilePath);
        Assert.True(Path.IsPathRooted(config.TypeStubsFilePath));
    }

    [Fact]
    public void ParseLines_parsesKindFullNameAndMembers()
    {
        IReadOnlyCollection<TypeStubEntry> entries = TypeStubsParser.ParseLines(
        [
            "# comment",
            "TYPE|Demo.Widget|Run,Stop",
            "METHOD|Demo.Widget.Run()|System.String",
            "Demo.PlainType",
        ]);

        TypeStubEntry[] items = entries.ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal("Demo.Widget", items[0].FullName);
        Assert.Equal("Demo.Widget.Run()", items[1].FullName);
        Assert.Equal("Demo.PlainType", items[2].FullName);

        TypeStubEntry typeEntry = items[0];
        Assert.Equal("TYPE", typeEntry.Kind);
        Assert.Equal(["Run", "Stop"], typeEntry.Members);
    }
}
