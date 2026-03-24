namespace TerrariaTools.Dome.Core.Cpg;

public sealed class TypeRelationsLayerCreator : LayerCreator
{
    public override string OverlayName => "typerel";

    public override string Description => "Type relations layer.";

    public override IReadOnlyList<string> DependsOn => ["base"];

    protected override void Create(CpgContext context)
    {
        new TypeHierarchyPass(context).CreateAndApply();
        new DeclarationTypeLinkerPass(context).CreateAndApply();
        new AliasLinkerPass(context).CreateAndApply();
        new FieldAccessLinkerPass(context).CreateAndApply();
    }
}
