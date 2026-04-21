using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;

namespace Logic.Analysis.Engine.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/TypeRelations.scala`。
/// </summary>
public sealed class TypeRelationsLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "typerel";


    public override string OverlayName => OverlayNameValue;


    public override string Description => "Type relations layer (hierarchy and aliases)";


    public override IReadOnlyList<string> DependsOn => new[] { BaseLayer.OverlayNameValue };


    public override IReadOnlyList<string> PassNames()
    {
        return new[] { "BuildTypeHierarchyPass", "BuildAliasRelationPass", "BuildFieldAccessRelationPass" };
    }


    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph)
    {
        return new CpgPass[] { new BuildTypeHierarchyPass(), new BuildAliasRelationPass(), new BuildFieldAccessRelationPass() };
    }
}
