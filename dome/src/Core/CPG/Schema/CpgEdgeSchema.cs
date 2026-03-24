namespace TerrariaTools.Dome.Core.Cpg;

public sealed record CpgEdgeSchema(
    string Label,
    string ClrName,
    IReadOnlyList<string>? SourceKinds,
    IReadOnlyList<string>? TargetKinds,
    string Layer)
{
    public IReadOnlyList<string> SourceKinds { get; } = SourceKinds ?? Array.Empty<string>();

    public IReadOnlyList<string> TargetKinds { get; } = TargetKinds ?? Array.Empty<string>();
}
