using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;
using Logic.Analysis.Engine.Passes.ControlFlow;
using Logic.Analysis.Engine.Passes.ControlFlow.Dominance;

namespace Logic.Analysis.Engine.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/ControlFlow.scala`。
/// </summary>
public sealed class ControlFlowLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "controlflow";


    public override string OverlayName => OverlayNameValue;


    public override string Description => "Control flow layer (including dominators and CDG edges)";


    public override IReadOnlyList<string> DependsOn => new[] { BaseLayer.OverlayNameValue };


    public override IReadOnlyList<string> PassNames()
    {
        return new[] { "BuildCfgPass", "CfgDominatorPass", "BuildCdgPass" };
    }


    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[] { new BuildCfgPass(), new CfgDominatorPass(), new BuildCdgPass() };
    }
}
