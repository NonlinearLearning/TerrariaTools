namespace TerrariaTools.Dome.Core.Cpg;

public sealed record CpgNodeSchema(
    string Label,
    string ClrName,
    string? PrimaryBase,
    IReadOnlyList<string>? RoleInterfaces,
    IReadOnlyList<string>? Properties,
    string Layer,
    bool IsAbstract = false)
{
    public IReadOnlyList<string> RoleInterfaces { get; } = RoleInterfaces ?? Array.Empty<string>();

    public IReadOnlyList<string> Properties { get; } = Properties ?? Array.Empty<string>();
}
