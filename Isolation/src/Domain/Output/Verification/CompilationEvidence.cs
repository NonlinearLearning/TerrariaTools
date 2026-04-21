namespace Domain.Output.Verification;

/// <summary>
/// 表示编译证据。
/// </summary>
public sealed class CompilationEvidence
{
    public CompilationEvidence(bool success, int diagnosticCount, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentOutOfRangeException.ThrowIfNegative(diagnosticCount);
        Success = success;
        DiagnosticCount = diagnosticCount;
        Summary = summary.Trim();
    }

    public bool Success { get; }

    public int DiagnosticCount { get; }

    public string Summary { get; }
}
