namespace TerrariaTools.Dome.Core.Cpg;

public sealed record CpgPropertySchema(
    string Name,
    string ValueKind,
    bool IsRequired = false);
