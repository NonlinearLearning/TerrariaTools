using Analysis.Core;
using Analysis.Semantic;
using Xunit;

namespace Analysis.Tests.Semantic;

public sealed class OverlaysTests
{
    [Fact]
    public void AppendOverlayName_addsOverlayToMetadata()
    {
        CpgGraph graph = new();
        CpgNode metaDataNode = graph.CreateNode(CpgNodeKind.MetaData);
        metaDataNode.SetProperty("Overlays", new List<string>());

        Overlays.AppendOverlayName(graph, "semanticcpg");
        Overlays.AppendOverlayName(graph, "dataflowOss");

        IReadOnlyList<string> overlays = Overlays.AppliedOverlays(graph);

        Assert.Equal(new[] { "semanticcpg", "dataflowOss" }, overlays);
    }

    [Fact]
    public void RemoveLastOverlayName_removesLastOverlay()
    {
        CpgGraph graph = new();
        CpgNode metaDataNode = graph.CreateNode(CpgNodeKind.MetaData);
        metaDataNode.SetProperty("Overlays", new List<string> { "semanticcpg", "dataflowOss" });

        Overlays.RemoveLastOverlayName(graph);

        IReadOnlyList<string> overlays = Overlays.AppliedOverlays(graph);
        Assert.Equal(new[] { "semanticcpg" }, overlays);
    }

    [Fact]
    public void PropertyAsString_readsPropertyFromNode()
    {
        CpgNode node = new(1, CpgNodeKind.Method);
        node.SetProperty("FullName", "Demo.Sample.Run()");

        Assert.Equal("Demo.Sample.Run()", node.PropertyAsString("FullName"));
        Assert.True(node.HasPropertyValue("FullName", "Demo.Sample.Run()"));
    }
}
