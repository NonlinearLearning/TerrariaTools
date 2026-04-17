using Analysis.Core;
using Analysis.Passes;

namespace Analysis.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/TypeRelations.scala`。
/// </summary>
public sealed class TypeRelationsLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "typerel";

    /// <inheritdoc />
    public override string OverlayName => OverlayNameValue;

    /// <inheritdoc />
    public override string Description => "Type relations layer (hierarchy and aliases)";

    /// <inheritdoc />
    public override IReadOnlyList<string> DependsOn => new[] { BaseLayer.OverlayNameValue };

    /// <inheritdoc />
    public override IReadOnlyList<string> PassNames()
    {
        return new[] { "BuildTypeHierarchyPass", "BuildAliasRelationPass", "BuildFieldAccessRelationPass" };
    }

    /// <inheritdoc />
    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[] { new BuildTypeHierarchyPass(), new BuildAliasRelationPass(), new BuildFieldAccessRelationPass() };
    }
}
