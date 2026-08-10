namespace NLISSN.Infrastructure.Configuration;

internal sealed class ConfigurationLoadException : ArgumentException
{
    internal ConfigurationLoadException(IReadOnlyList<ConfigurationDiagnostic> diagnostics)
        : base(string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
            $"{diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}")))
    {
        Diagnostics = diagnostics;
    }

    internal IReadOnlyList<ConfigurationDiagnostic> Diagnostics { get; }
}
