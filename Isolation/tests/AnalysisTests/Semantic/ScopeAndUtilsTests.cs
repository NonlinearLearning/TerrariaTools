using Analysis.Core;
using Analysis.Semantic.Utils;
using Analysis.X2Cpg.DataStructures;
using Xunit;

namespace Analysis.Tests.Semantic;

public sealed class ScopeAndUtilsTests
{
    [Fact]
    public void Scope_lookupUsesNearestVariable()
    {
        Scope<string, string, string> scope = new();

        scope.PushNewScope("method");
        scope.AddToScope("value", "outer");
        scope.PushNewScope("block");
        scope.AddToScope("value", "inner");

        Assert.Equal("inner", scope.LookupVariable("value"));
        Assert.Equal("block", scope.PopScope());
        Assert.Equal("outer", scope.LookupVariable("value"));
    }

    [Fact]
    public void VariableScopeManager_resolvesPendingReferencesWhenVariableIsAdded()
    {
        CpgGraph graph = new();
        CpgNode method = graph.CreateNode(CpgNodeKind.Method);
        CpgNode identifier = graph.CreateNode(CpgNodeKind.Identifier);
        identifier.SetProperty("Name", "value");
        CpgNode local = graph.CreateNode(CpgNodeKind.Local);
        local.SetProperty("Name", "value");
        VariableScopeManager manager = new();

        manager.PushNewMethodScope("Demo.Run", "Run", method);
        manager.AddReference("value", identifier, "int", "BY_VALUE");
        manager.AddVariable("value", local, "int", "BY_VALUE");
        manager.ResolvePendingReferences(graph);

        Assert.Contains(graph.GetOutgoingEdges(identifier.Id, CpgEdgeKind.Ref), edge => edge.TargetId == local.Id);
    }

    [Fact]
    public void VariableScopeManager_createsCaptureEdgesForOuterVariableReferences()
    {
        CpgGraph graph = new();
        CpgNode outerMethod = graph.CreateNode(CpgNodeKind.Method);
        CpgNode innerMethod = graph.CreateNode(CpgNodeKind.Method);
        CpgNode captureRef = graph.CreateNode(CpgNodeKind.Identifier);
        CpgNode local = graph.CreateNode(CpgNodeKind.Local);
        local.SetProperty("Name", "value");
        CpgNode identifier = graph.CreateNode(CpgNodeKind.Identifier);
        identifier.SetProperty("Name", "value");
        VariableScopeManager manager = new();

        manager.PushNewMethodScope("Demo.Outer", "Outer", outerMethod);
        manager.AddVariable("value", local, "int", "BY_REFERENCE");
        manager.PushNewMethodScope("Demo.Outer.Inner", "Inner", innerMethod, captureRef);
        manager.AddReference("value", identifier, "int", "BY_REFERENCE");
        manager.ResolvePendingReferences(graph);

        Assert.Contains(graph.GetOutgoingEdges(identifier.Id, CpgEdgeKind.Ref), edge => edge.TargetId == local.Id);
        Assert.Contains(graph.GetOutgoingEdges(captureRef.Id, CpgEdgeKind.Capture), edge => edge.TargetId == local.Id);
    }

    [Theory]
    [InlineData("<operator>.fieldAccess")]
    [InlineData("<operator>.memberAccess")]
    [InlineData("<operator>.indexAccess")]
    public void MemberAccess_recognizesGenericMemberAccess(string name)
    {
        Assert.True(MemberAccess.IsGenericMemberAccessName(name));
        Assert.True(MemberAccess.IsFieldAccess(name));
    }

    [Fact]
    public void Statements_countsTopLevelMethodExpressions()
    {
        CpgGraph graph = new();
        CpgNode method = graph.CreateNode(CpgNodeKind.Method);
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        CpgNode nestedIdentifier = graph.CreateNode(CpgNodeKind.Identifier);
        _ = graph.AddEdge(method.Id, call.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(call.Id, nestedIdentifier.Id, CpgEdgeKind.Ast);

        Assert.Equal(1, Statements.CountAll(graph));
    }
}
