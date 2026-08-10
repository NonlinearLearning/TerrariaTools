namespace NLISSN.Infrastructure.Configuration;

internal sealed record ConfigurationLoadResult(
    AnalysisConfiguration? Configuration,
    IReadOnlyList<ConfigurationDiagnostic> Diagnostics)
{
    internal bool IsSuccess => Configuration is not null && Diagnostics.Count == 0;

    internal AnalysisConfiguration RequireConfiguration()
    {
        return IsSuccess
            ? Configuration!
            : throw new ConfigurationLoadException(Diagnostics);
    }
}
