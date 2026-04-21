using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 引用映射 DTO。
/// </summary>
public sealed class ReferenceMappingDto
{
    public string SourceReference { get; set; } = string.Empty;

    public string TargetReference { get; set; } = string.Empty;
}
