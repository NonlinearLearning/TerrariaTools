using Analysis.Core;
using Analysis.Passes;
using Analysis.Passes.DataFlow;

namespace Analysis.Layers.DataFlow;

/// <summary>
/// OSS 数据流 overlay。
///
/// 对应 Joern `dataflowengineoss/layers/dataflows/OssDataFlow.scala`。
/// </summary>
public sealed class OssDataFlowLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = BuildOssDataFlowPass.OverlayName;

    /// <inheritdoc />
    public override string OverlayName => OverlayNameValue;

    /// <inheritdoc />
    public override string Description => "Layer to support the OSS lightweight data flow tracker";

    /// <inheritdoc />
    public override IReadOnlyList<string> PassNames()
    {
        return new[] { "ReachingDefPass" };
    }

    /// <inheritdoc />
    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[] { new BuildOssDataFlowPass() };
    }
}
