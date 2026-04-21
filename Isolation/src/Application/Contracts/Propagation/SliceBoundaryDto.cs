using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 切片边界 DTO。
/// </summary>
public sealed class SliceBoundaryDto
{
    public string BoundaryName { get; set; } = string.Empty;

    public ContractSliceDirection Direction { get; set; }

    public int MaxDepth { get; set; }

    public bool IncludeExternalReferences { get; set; }
}
