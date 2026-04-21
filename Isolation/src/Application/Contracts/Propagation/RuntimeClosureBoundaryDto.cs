using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 最小运行闭包传播边界 DTO。
/// </summary>
public sealed class RuntimeClosureBoundaryDto
{
    public ClosureRootDto Root { get; set; } = new();

    public ContractClosureIntegrityStatus IntegrityStatus { get; set; }

    public IReadOnlyCollection<ReferenceMappingDto> ReferenceMappings { get; set; } = Array.Empty<ReferenceMappingDto>();
}
