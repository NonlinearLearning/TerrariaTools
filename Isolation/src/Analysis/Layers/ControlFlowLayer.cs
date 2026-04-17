using Analysis.Core;
using Analysis.Passes;
using Analysis.Passes.ControlFlow;
using Analysis.Passes.ControlFlow.Dominance;

namespace Analysis.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/ControlFlow.scala`。
/// </summary>
public sealed class ControlFlowLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "controlflow";

    /// <inheritdoc />
    public override string OverlayName => OverlayNameValue;

    /// <inheritdoc />
    public override string Description => "Control flow layer (including dominators and CDG edges)";

    /// <inheritdoc />
    public override IReadOnlyList<string> DependsOn => new[] { BaseLayer.OverlayNameValue };

    /// <inheritdoc />
    public override IReadOnlyList<string> PassNames()
    {
        return new[] { "BuildCfgPass", "CfgDominatorPass", "BuildCdgPass" };
    }

    /// <inheritdoc />
    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[] { new BuildCfgPass(), new CfgDominatorPass(), new BuildCdgPass() };
    }
}
