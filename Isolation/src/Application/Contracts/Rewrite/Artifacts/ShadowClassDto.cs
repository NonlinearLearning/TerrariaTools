using System.ComponentModel;
using Application.Contracts.Propagation;

namespace Application.Contracts.Rewrite.Artifacts;

/// <summary>
/// 影子类 DTO。
/// </summary>
public sealed class ShadowClassDto
{
    private ShadowBoundaryDto boundary = new();

    public string ClassName { get; set; } = string.Empty;

    public string ShadowClassName { get; set; } = string.Empty;

    public string SourceCode { get; set; } = string.Empty;

    /// <summary>
    /// 影子类传播边界。作为唯一主表达保留。
    /// </summary>
    public ShadowBoundaryDto Boundary
    {
        get => boundary;
        set => boundary = value ?? new ShadowBoundaryDto();
    }

    public IReadOnlyCollection<string> MemberNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 兼容旧调用方的扁平字段，请改用 Boundary.ReferenceMappings。
    /// </summary>
    [Obsolete("Use Boundary.ReferenceMappings instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IReadOnlyCollection<ReferenceMappingDto> ReferenceMappings
    {
        get => Boundary.ReferenceMappings;
        set => Boundary = new ShadowBoundaryDto
        {
            ReferenceMappings = value ?? Array.Empty<ReferenceMappingDto>(),
        };
    }
}
