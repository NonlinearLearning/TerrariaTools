using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;
using Logic.Analysis.Engine.Passes.DataFlow;

namespace Logic.Analysis.Engine.Layers.DataFlow;

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


    public override string OverlayName => OverlayNameValue;


    public override string Description => "Layer to support the OSS lightweight data flow tracker";


    public override IReadOnlyList<string> PassNames()
    {
        return new[] { "ReachingDefPass" };
    }


    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[] { new BuildOssDataFlowPass() };
    }
}
