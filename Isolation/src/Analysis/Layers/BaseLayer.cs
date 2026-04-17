using Analysis.Core;
using Analysis.Passes;

namespace Analysis.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/Base.scala`。
/// </summary>
public sealed class BaseLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "base";

    /// <inheritdoc />
    public override string OverlayName => OverlayNameValue;

    /// <inheritdoc />
    public override string Description => "base layer (linked frontend CPG)";

    /// <inheritdoc />
    public override IReadOnlyList<string> PassNames()
    {
        return new[]
        {
            "BuildFileNodesPass",
            "BuildNamespaceNodesPass",
            "BuildTypeStubPass",
            "BuildMethodStubPass",
            "BuildParameterIndexCompatPass",
            "BuildMethodDecoratorPass",
            "LinkAstPass",
            "BuildContainsEdgesPass",
            "ResolveTypeRefsPass",
            "EvaluateNodeTypesPass",
        };
    }

    /// <inheritdoc />
    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[]
        {
            new BuildParameterIndexCompatPass(),
            new BuildMethodDecoratorPass(),
            new LinkAstPass(),
            new BuildContainsEdgesPass(),
            new ResolveTypeRefsPass(),
            new EvaluateNodeTypesPass(),
        };
    }
}
