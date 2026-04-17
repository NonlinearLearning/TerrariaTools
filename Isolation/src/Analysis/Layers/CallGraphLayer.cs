using Analysis.Core;
using Analysis.Passes;

namespace Analysis.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/CallGraph.scala`。
/// </summary>
public sealed class CallGraphLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "callgraph";

    /// <inheritdoc />
    public override string OverlayName => OverlayNameValue;

    /// <inheritdoc />
    public override string Description => "Call graph layer";

    /// <inheritdoc />
    public override IReadOnlyList<string> DependsOn => new[] { TypeRelationsLayer.OverlayNameValue };

    /// <inheritdoc />
    public override IReadOnlyList<string> PassNames()
    {
        return new[] { "BuildMethodReferencePass", "BuildStaticCallGraphPass", "BuildDynamicCallGraphPass" };
    }

    /// <inheritdoc />
    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[] { new BuildMethodReferencePass(), new BuildStaticCallGraphPass(), new BuildDynamicCallGraphPass() };
    }
}
