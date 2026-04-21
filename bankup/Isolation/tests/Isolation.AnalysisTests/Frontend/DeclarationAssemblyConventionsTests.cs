using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class DeclarationAssemblyConventionsTests
{
    [Fact]
    public void ApplyTypeDeclProperties_writesTypeShape()
    {
        CpgNode node = new(1, CpgNodeKind.TypeDecl);

        DeclarationAssemblyConventions.ApplyTypeDeclProperties(
            node,
            2,
            "demo.cs",
            "sym",
            "Demo.Type",
            "Class",
            new[] { "Base" });

        Assert.True(node.TryGetProperty<string>("TypeFullName", out string? typeFullName));
        Assert.True(node.TryGetProperty<string>("TypeKind", out string? typeKind));
        Assert.Equal("Demo.Type", typeFullName);
        Assert.Equal("Class", typeKind);
    }

    [Fact]
    public void ApplyMemberIdentity_writesNameFullNameAndOptionalSource()
    {
        CpgNode node = new(1, CpgNodeKind.Member);

        DeclarationAssemblyConventions.ApplyMemberIdentity(node, "Id", "Demo.Id", "Generated");

        Assert.True(node.TryGetProperty<string>("Name", out string? name));
        Assert.True(node.TryGetProperty<string>("Source", out string? source));
        Assert.Equal("Id", name);
        Assert.Equal("Generated", source);
    }
}
