namespace TerrariaTools.Dome.Core.Cpg;

public sealed record CpgLayerSchema(
    string Name,
    string Description,
    IReadOnlyList<string>? DependsOn = null)
{
    public IReadOnlyList<string> DependsOn { get; } = DependsOn ?? Array.Empty<string>();
}
