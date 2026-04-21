using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class MethodNodeConventionsTests
{
    [Fact]
    public void ApplyMethodProperties_writesStableMethodShape()
    {
        CpgNode node = new(1, CpgNodeKind.Method);
        MethodNodeDescriptor descriptor = new(
            "Demo",
            "Demo.Full",
            "void()",
            "void",
            "sym-1",
            "DemoType",
            false,
            true,
            false,
            9,
            "demo.cs");

        MethodNodeConventions.ApplyMethodProperties(node, descriptor);

        Assert.True(node.TryGetProperty<string>("Name", out string? name));
        Assert.True(node.TryGetProperty<string>("FullName", out string? fullName));
        Assert.True(node.TryGetProperty<string>("FileName", out string? fileName));
        Assert.Equal("Demo", name);
        Assert.Equal("Demo.Full", fullName);
        Assert.Equal("demo.cs", fileName);
    }

    [Fact]
    public void ApplyMethodParameterOrder_writesIndexAndOrder()
    {
        CpgNode node = new(1, CpgNodeKind.MethodParameterIn);

        MethodNodeConventions.ApplyMethodParameterOrder(node, "value", 2);

        Assert.True(node.TryGetProperty<string>("Name", out string? name));
        Assert.True(node.TryGetProperty<int>("Index", out int index));
        Assert.True(node.TryGetProperty<int>("Order", out int order));
        Assert.Equal("value", name);
        Assert.Equal(2, index);
        Assert.Equal(2, order);
    }

    [Fact]
    public void ApplyMethodReturnProperties_writesReturnShape()
    {
        CpgNode node = new(1, CpgNodeKind.MethodReturn);

        MethodNodeConventions.ApplyMethodReturnProperties(node, "string", 4, 3);

        Assert.True(node.TryGetProperty<string>("TypeFullName", out string? typeFullName));
        Assert.True(node.TryGetProperty<long>("AstParentId", out long astParentId));
        Assert.True(node.TryGetProperty<int>("Order", out int order));
        Assert.Equal("string", typeFullName);
        Assert.Equal(4L, astParentId);
        Assert.Equal(3, order);
    }
}
