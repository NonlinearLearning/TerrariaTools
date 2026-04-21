namespace Application.Contracts.Output.Verification;

/// <summary>
/// 编译证据 DTO。
/// </summary>
public sealed class CompilationEvidenceDto
{
    public bool Success { get; set; }

    public int DiagnosticCount { get; set; }

    public string Summary { get; set; } = string.Empty;
}
