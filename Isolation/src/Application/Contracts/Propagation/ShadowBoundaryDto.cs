using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 影子类传播边界 DTO。
/// </summary>
public sealed class ShadowBoundaryDto
{
    public IReadOnlyCollection<ReferenceMappingDto> ReferenceMappings { get; set; } = Array.Empty<ReferenceMappingDto>();
}
