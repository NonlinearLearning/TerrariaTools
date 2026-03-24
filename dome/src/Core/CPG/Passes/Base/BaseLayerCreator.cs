namespace TerrariaTools.Dome.Core.Cpg;

public sealed class BaseLayerCreator : LayerCreator
{
    public override string OverlayName => "base";

    public override string Description => "Base layer.";

    protected override void Create(CpgContext context)
    {
        new FileCreationPass(context).CreateAndApply();
        new NamespaceCreatorPass(context).CreateAndApply();
        new TypeDeclStubCreatorPass(context).CreateAndApply();
        new MethodStubCreatorPass(context).CreateAndApply();
        new MethodDecoratorPass(context).CreateAndApply();
        new AstLinkerPass(context).CreateAndApply();
        new ContainsEdgePass(context).CreateAndApply();
        new TypeRefPass(context).CreateAndApply();
        new IdentifierRefPass(context).CreateAndApply();
        new TypeEvalPass(context).CreateAndApply();
    }
}
