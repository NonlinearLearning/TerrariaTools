using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;

namespace Logic.Analysis.Engine.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/Base.scala`。
/// </summary>
public sealed class BaseLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "base";


    public override string OverlayName => OverlayNameValue;


    public override string Description => "base layer (linked frontend CPG)";


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
