using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Frontend;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class NodeAssemblyConventionsTests
{
    [Fact]
    public void ApplyDeclarationProperties_writesDeclarationShape()
    {
        CpgNode node = new(1, CpgNodeKind.Local);

        NodeAssemblyConventions.ApplyDeclarationProperties(node, "System.String", "sym", 4, "demo.cs");

        Assert.True(node.TryGetProperty<string>("TypeFullName", out string? typeFullName));
        Assert.True(node.TryGetProperty<string>("FileName", out string? fileName));
        Assert.Equal("System.String", typeFullName);
        Assert.Equal("demo.cs", fileName);
    }

    [Fact]
    public void ApplyControlNodeProperties_writesControlShape()
    {
        CpgNode node = new(1, CpgNodeKind.ControlStructure);

        NodeAssemblyConventions.ApplyControlNodeProperties(node, "IF", 2);

        Assert.True(node.TryGetProperty<string>("Name", out string? name));
        Assert.True(node.TryGetProperty<long>("AstParentId", out long astParentId));
        Assert.Equal("IF", name);
        Assert.Equal(2L, astParentId);
    }

    [Fact]
    public void ApplyCallNodeProperties_writesCallShape()
    {
        CpgNode node = new(1, CpgNodeKind.Call);

        NodeAssemblyConventions.ApplyCallNodeProperties(
            node,
            new CallNodeDescriptor("Call", "obj.Call()", "Obj.Call", "void()", "void", "DYNAMIC_DISPATCH", "sym", "op-1", 3, "demo.cs"));

        Assert.True(node.TryGetProperty<string>("Name", out string? name));
        Assert.True(node.TryGetProperty<string>("OperationId", out string? operationId));
        Assert.Equal("Call", name);
        Assert.Equal("op-1", operationId);
    }
}
