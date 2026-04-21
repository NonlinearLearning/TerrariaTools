using Application.Contracts;

namespace Application.Contracts.Execution;

/// <summary>
/// 文件变更 DTO。
/// </summary>
public sealed class FileChangeDto
{
    public string DocumentPath { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public IReadOnlyCollection<string> AffectedTargets { get; set; } = Array.Empty<string>();
}
