using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 传播事实引用 DTO。
/// </summary>
public sealed class PropagationFactReferenceDto
{
    public string SourceNodeId { get; set; } = string.Empty;

    public string TargetNodeId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;
}
