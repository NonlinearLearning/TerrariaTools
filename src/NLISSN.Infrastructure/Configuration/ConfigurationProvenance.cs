namespace NLISSN.Infrastructure.Configuration;

internal sealed record ConfigurationProvenance(
    int SourceSchemaVersion,
    int TargetSchemaVersion,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyDictionary<string, string> FieldOrigins);
