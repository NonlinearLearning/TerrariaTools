using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;

namespace Logic.Analysis.Engine.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/CallGraph.scala`。
/// </summary>
public sealed class CallGraphLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "callgraph";


    public override string OverlayName => OverlayNameValue;


    public override string Description => "Call graph layer";


    public override IReadOnlyList<string> DependsOn => new[] { TypeRelationsLayer.OverlayNameValue };


    public override IReadOnlyList<string> PassNames()
    {
        return new[] { "BuildMethodReferencePass", "BuildStaticCallGraphPass", "BuildDynamicCallGraphPass" };
    }


    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[] { new BuildMethodReferencePass(), new BuildStaticCallGraphPass(), new BuildDynamicCallGraphPass() };
    }
}
