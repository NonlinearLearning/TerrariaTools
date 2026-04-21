using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class ExpressionAssemblyConventionsTests
{
    [Fact]
    public void ApplyIdentifierProperties_writesShape()
    {
        CpgNode node = new(1, CpgNodeKind.Identifier);

        ExpressionAssemblyConventions.ApplyIdentifierProperties(node, "x", "x", "int", "sym", 2, "demo.cs");

        Assert.True(node.TryGetProperty<string>("Name", out string? name));
        Assert.True(node.TryGetProperty<string>("TypeFullName", out string? typeFullName));
        Assert.Equal("x", name);
        Assert.Equal("int", typeFullName);
    }

    [Fact]
    public void ApplyMethodRefProperties_writesShape()
    {
        CpgNode node = new(1, CpgNodeKind.MethodRef);

        ExpressionAssemblyConventions.ApplyMethodRefProperties(node, "Run", "Run", "Demo.Run", "void()", "sym", 3, "demo.cs");

        Assert.True(node.TryGetProperty<string>("MethodFullName", out string? fullName));
        Assert.True(node.TryGetProperty<string>("FileName", out string? fileName));
        Assert.Equal("Demo.Run", fullName);
        Assert.Equal("demo.cs", fileName);
    }
}
