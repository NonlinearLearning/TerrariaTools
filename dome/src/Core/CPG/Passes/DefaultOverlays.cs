namespace TerrariaTools.Dome.Core.Cpg;

public static class DefaultOverlays
{
    public static void Apply(DomeCpg cpg, CpgContext context)
    {
        ApplyBase(cpg, context);
        ApplyControlFlow(cpg, context);
        ApplyTypeRelations(cpg, context);
        ApplyCallGraph(cpg, context);
    }

    public static void ApplyBase(DomeCpg cpg, CpgContext context)
    {
        _ = cpg;
        new BaseLayerCreator().Run(context);
    }

    public static void ApplyControlFlow(DomeCpg cpg, CpgContext context)
    {
        _ = cpg;
        new ControlFlowLayerCreator().Run(context);
    }

    public static void ApplyTypeRelations(DomeCpg cpg, CpgContext context)
    {
        _ = cpg;
        new TypeRelationsLayerCreator().Run(context);
    }

    public static void ApplyCallGraph(DomeCpg cpg, CpgContext context)
    {
        _ = cpg;
        new CallGraphLayerCreator().Run(context);
    }
}
