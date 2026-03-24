namespace TerrariaTools.Dome.Core.Cpg;

public sealed class ControlFlowLayerCreator : LayerCreator
{
    public override string OverlayName => "controlflow";

    public override string Description => "Control flow layer.";

    public override IReadOnlyList<string> DependsOn => ["base"];

    protected override void Create(CpgContext context)
    {
        new CfgCreationPass(context).CreateAndApply();
        new DominatorPass(context).CreateAndApply();
        new CdgPass(context).CreateAndApply();
    }
}
