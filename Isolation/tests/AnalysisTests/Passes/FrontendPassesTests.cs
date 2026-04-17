using Analysis.Core;
using Analysis.Passes;
using Xunit;

namespace Analysis.Tests.Passes;

public sealed class FrontendPassesTests
{
    [Fact]
    public void BuildTypeNodePass_createsTypeNodesFromNodeProperties()
    {
        CpgGraph graph = new();
        CpgNode localNode = graph.CreateNode(CpgNodeKind.Local);
        localNode.SetProperty("TypeFullName", "Demo.Widget");

        new BuildTypeNodePass().Run(graph);

        CpgNode typeNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Type).Where(node =>
                node.TryGetProperty<string>("FullName", out string? fullName) &&
                string.Equals(fullName, "Demo.Widget", StringComparison.Ordinal)));
        Assert.Equal("Widget", typeNode.TryGetProperty<string>("Name", out string? name) ? name : string.Empty);
    }

    [Fact]
    public void BuildInheritanceFullNamePass_resolvesUniqueShortBaseTypeName()
    {
        CpgGraph graph = new();

        CpgNode baseTypeDecl = graph.CreateNode(CpgNodeKind.TypeDecl);
        baseTypeDecl.SetProperty("Name", "BaseWorker");
        baseTypeDecl.SetProperty("FullName", "Demo.BaseWorker");

        CpgNode childTypeDecl = graph.CreateNode(CpgNodeKind.TypeDecl);
        childTypeDecl.SetProperty("Name", "FancyWorker");
        childTypeDecl.SetProperty("FullName", "Demo.FancyWorker");
        childTypeDecl.SetProperty("InheritsFromTypeFullNames", new[] { "BaseWorker" });

        new BuildInheritanceFullNamePass().Run(graph);

        Assert.True(childTypeDecl.TryGetProperty<IReadOnlyCollection<string>>("InheritsFromTypeFullNames", out IReadOnlyCollection<string>? resolved));
        Assert.Contains("Demo.BaseWorker", resolved ?? Array.Empty<string>());
    }

    [Fact]
    public void BuildTypeHintCallLinkerPass_linksCallToExistingMethod()
    {
        CpgGraph graph = new();

        CpgNode methodNode = graph.CreateNode(CpgNodeKind.Method);
        methodNode.SetProperty("Name", "Run");
        methodNode.SetProperty("FullName", "Demo.Worker.Run()");

        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", "Run");
        callNode.SetProperty("DynamicTypeHintFullNames", new[] { "Demo.Worker.Run()" });

        new BuildTypeHintCallLinkerPass().Run(graph);

        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == methodNode.Id);
        Assert.True(callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName));
        Assert.Equal("Demo.Worker.Run()", methodFullName);
    }

    [Fact]
    public void BuildTypeHintCallLinkerPass_createsExternalStubWhenMethodIsMissing()
    {
        CpgGraph graph = new();

        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", "Run");
        callNode.SetProperty("DynamicTypeHintFullNames", new[] { "Demo.Worker.Run()" });

        new BuildTypeHintCallLinkerPass().Run(graph);

        CpgNode methodNode = Assert.Single(
            graph.GetNodes(CpgNodeKind.Method).Where(node =>
                node.TryGetProperty<string>("FullName", out string? fullName) &&
                string.Equals(fullName, "Demo.Worker.Run()", StringComparison.Ordinal)));
        Assert.True(methodNode.TryGetProperty<bool>("IsExternal", out bool isExternal) && isExternal);
        Assert.Contains(
            graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call),
            edge => edge.TargetId == methodNode.Id);
    }

    [Fact]
    public void BuildParameterIndexCompatPass_copiesOrderToMissingIndex()
    {
        CpgGraph graph = new();
        CpgNode parameterNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        parameterNode.SetProperty("Order", 2);

        new BuildParameterIndexCompatPass().Run(graph);

        Assert.True(parameterNode.TryGetProperty<int>("Index", out int index));
        Assert.Equal(2, index);
    }

    [Fact]
    public void BuildMethodDecoratorPass_createsParameterOutAndParameterLink()
    {
        CpgGraph graph = new();
        CpgNode methodNode = graph.CreateNode(CpgNodeKind.Method);

        CpgNode parameterInNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        parameterInNode.SetProperty("Name", "value");
        parameterInNode.SetProperty("Order", 1);
        parameterInNode.SetProperty("Index", 1);
        parameterInNode.SetProperty("TypeFullName", "int");
        parameterInNode.SetProperty("AstParentId", methodNode.Id);

        new BuildMethodDecoratorPass().Run(graph);
        new LinkAstPass().Run(graph);

        CpgNode parameterOutNode = Assert.Single(graph.GetNodes(CpgNodeKind.MethodParameterOut));
        Assert.True(parameterOutNode.TryGetProperty<string>("Name", out string? name));
        Assert.Equal("value", name);
        Assert.Contains(
            graph.GetOutgoingEdges(parameterInNode.Id, CpgEdgeKind.ParameterLink),
            edge => edge.TargetId == parameterOutNode.Id);
        Assert.Contains(
            graph.GetOutgoingEdges(methodNode.Id, CpgEdgeKind.Ast),
            edge => edge.TargetId == parameterOutNode.Id);
    }
}
