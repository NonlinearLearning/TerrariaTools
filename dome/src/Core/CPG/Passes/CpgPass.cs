namespace TerrariaTools.Dome.Core.Cpg;

public abstract class CpgPass(CpgContext context)
{
    protected CpgContext Context { get; } = context;

    public void CreateAndApply()
    {
        DiffGraph diff = new();
        Apply(diff);
        DiffGraphApplier.Apply(Context.Cpg, diff);
    }

    protected abstract void Apply(DiffGraph diff);
}
