namespace TerrariaTools.Dome.Core.Cpg;

public sealed record CpgSchema(
    IReadOnlyList<CpgLayerSchema>? Layers = null,
    IReadOnlyList<CpgNodeSchema>? Nodes = null,
    IReadOnlyList<CpgEdgeSchema>? Edges = null,
    IReadOnlyList<CpgPropertySchema>? Properties = null)
{
    public IReadOnlyList<CpgLayerSchema> Layers { get; } = Layers ?? Array.Empty<CpgLayerSchema>();

    public IReadOnlyList<CpgNodeSchema> Nodes { get; } = Nodes ?? Array.Empty<CpgNodeSchema>();

    public IReadOnlyList<CpgEdgeSchema> Edges { get; } = Edges ?? Array.Empty<CpgEdgeSchema>();

    public IReadOnlyList<CpgPropertySchema> Properties { get; } = Properties ?? Array.Empty<CpgPropertySchema>();
}
