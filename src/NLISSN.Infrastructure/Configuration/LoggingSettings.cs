namespace NLISSN.Infrastructure.Configuration;

internal sealed record LoggingSettings(
    string Profile,
    string Level,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Events,
    string View);
