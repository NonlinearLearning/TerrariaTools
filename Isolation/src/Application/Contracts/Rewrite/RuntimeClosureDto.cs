using Application.Contracts.Propagation;
using Domain.Propagation;

namespace Application.Contracts.Rewrite;

/// <summary>
/// 最小运行闭包 DTO。
/// </summary>
public sealed class RuntimeClosureDto
{
    public string ClassName { get; set; } = string.Empty;

    public string RootMethodName { get; set; } = string.Empty;

    public string ClosureClassName { get; set; } = string.Empty;

    public string SourceCode { get; set; } = string.Empty;

    public IReadOnlyCollection<string> MemberNames { get; set; } = Array.Empty<string>();

    public ClosureIntegrityStatus IntegrityStatus { get; set; }

    public IReadOnlyCollection<ReferenceMappingDto> ReferenceMappings { get; set; } =
        Array.Empty<ReferenceMappingDto>();
}
