using System.ComponentModel;
using Application.Contracts;
using Application.Contracts.Propagation;

namespace Application.Contracts.Rewrite.Artifacts;

/// <summary>
/// 最小运行闭包 DTO。
/// </summary>
public sealed class RuntimeClosureDto
{
    private RuntimeClosureBoundaryDto boundary = new();

    public string ClassName { get; set; } = string.Empty;

    public string ClosureClassName { get; set; } = string.Empty;

    public string SourceCode { get; set; } = string.Empty;

    /// <summary>
    /// 运行闭包传播边界。作为唯一主表达保留。
    /// </summary>
    public RuntimeClosureBoundaryDto Boundary
    {
        get => boundary;
        set => boundary = value ?? new RuntimeClosureBoundaryDto();
    }

    public IReadOnlyCollection<string> MemberNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 兼容旧调用方的扁平字段，请改用 Boundary.Root.MemberName。
    /// </summary>
    [Obsolete("Use Boundary.Root.MemberName instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string RootMethodName
    {
        get => Boundary.Root.MemberName;
        set => Boundary = new RuntimeClosureBoundaryDto
        {
            Root = new ClosureRootDto
            {
                ClassName = string.IsNullOrWhiteSpace(Boundary.Root.ClassName) ? ClassName : Boundary.Root.ClassName,
                MemberName = value ?? string.Empty,
            },
            IntegrityStatus = Boundary.IntegrityStatus,
            ReferenceMappings = Boundary.ReferenceMappings,
        };
    }

    /// <summary>
    /// 兼容旧调用方的扁平字段，请改用 Boundary.IntegrityStatus。
    /// </summary>
    [Obsolete("Use Boundary.IntegrityStatus instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ContractClosureIntegrityStatus IntegrityStatus
    {
        get => Boundary.IntegrityStatus;
        set => Boundary = new RuntimeClosureBoundaryDto
        {
            Root = Boundary.Root,
            IntegrityStatus = value,
            ReferenceMappings = Boundary.ReferenceMappings,
        };
    }

    [Obsolete("Use Boundary.IntegrityStatus instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int IntegrityStatusValue
    {
        get => (int)Boundary.IntegrityStatus;
        set => Boundary = new RuntimeClosureBoundaryDto
        {
            Root = Boundary.Root,
            IntegrityStatus = Enum.IsDefined(typeof(ContractClosureIntegrityStatus), value)
                ? (ContractClosureIntegrityStatus)value
                : ContractClosureIntegrityStatus.Unknown,
            ReferenceMappings = Boundary.ReferenceMappings,
        };
    }

    /// <summary>
    /// 兼容旧调用方的扁平字段，请改用 Boundary.ReferenceMappings。
    /// </summary>
    [Obsolete("Use Boundary.ReferenceMappings instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IReadOnlyCollection<ReferenceMappingDto> ReferenceMappings
    {
        get => Boundary.ReferenceMappings;
        set => Boundary = new RuntimeClosureBoundaryDto
        {
            Root = Boundary.Root,
            IntegrityStatus = Boundary.IntegrityStatus,
            ReferenceMappings = value ?? Array.Empty<ReferenceMappingDto>(),
        };
    }
}
