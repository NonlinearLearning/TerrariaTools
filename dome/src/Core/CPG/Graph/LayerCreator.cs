namespace TerrariaTools.Dome.Core.Cpg;

public abstract class LayerCreator
{
    public abstract string OverlayName { get; }

    public abstract string Description { get; }

    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public void Run(CpgContext context)
    {
        if (context.Cpg.MetaData.Overlays.Contains(OverlayName, StringComparer.Ordinal))
        {
            return;
        }

        IEnumerable<string> missingDependencies = DependsOn.Where(
            dependency => !context.Cpg.MetaData.Overlays.Contains(dependency, StringComparer.Ordinal));
        if (missingDependencies.Any())
        {
            return;
        }

        Create(context);
        context.Cpg.MetaData.AppendOverlay(OverlayName);
    }

    protected abstract void Create(CpgContext context);
}
