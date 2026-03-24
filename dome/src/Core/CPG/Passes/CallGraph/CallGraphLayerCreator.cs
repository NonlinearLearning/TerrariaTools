namespace TerrariaTools.Dome.Core.Cpg;

public sealed class CallGraphLayerCreator : LayerCreator
{
    public override string OverlayName => "callgraph";

    public override string Description => "Call graph layer.";

    public override IReadOnlyList<string> DependsOn => ["typerel"];

    protected override void Create(CpgContext context)
    {
        new MethodRefLinkerPass(context).CreateAndApply();
        new StaticCallLinkerPass(context).CreateAndApply();
        new DynamicCallLinkerPass(context).CreateAndApply();
    }
}
