using Application.Contracts;

namespace Application.Contracts.Rewrite.Artifacts;

/// <summary>
/// 代码重写结果 DTO。
/// </summary>
public sealed class CodeRewriteResultDto
{
    public ContractCodeRewriteKind RewriteKind { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public string SourceCode { get; set; } = string.Empty;

    public bool Changed { get; set; }

    public IReadOnlyCollection<string> Diagnostics { get; set; } = Array.Empty<string>();
}
