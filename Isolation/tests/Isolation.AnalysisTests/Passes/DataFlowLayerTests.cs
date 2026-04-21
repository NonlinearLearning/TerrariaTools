using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Layers.DataFlow;
using Domain.Analysis.Engine.Semantic;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class DataFlowLayerTests
{
    [Fact]
    public void OssDataFlowLayer_appliesDataflowOverlay()
    {
        CpgGraph graph = new();
        _ = graph.CreateNode(CpgNodeKind.MetaData);

        new OssDataFlowLayer().Apply(graph);

        Assert.Contains(OssDataFlowLayer.OverlayNameValue, Overlays.AppliedOverlays(graph));
    }

}
