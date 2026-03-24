namespace TerrariaTools.Dome.Core.Cpg;

public sealed class CpgContext(DomeCpg cpg, CpgSchema schema)
{
    public DomeCpg Cpg { get; } = cpg;

    public CpgSchema Schema { get; } = schema;
}
