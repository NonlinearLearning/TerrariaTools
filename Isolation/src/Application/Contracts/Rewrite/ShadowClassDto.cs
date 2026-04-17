using Application.Contracts.Propagation;

namespace Application.Contracts.Rewrite;

/// <summary>
/// 影子类 DTO。
/// </summary>
public sealed class ShadowClassDto
{
    public string ClassName { get; set; } = string.Empty;

    public string ShadowClassName { get; set; } = string.Empty;

    public string SourceCode { get; set; } = string.Empty;

    public IReadOnlyCollection<string> MemberNames { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<ReferenceMappingDto> ReferenceMappings { get; set; } =
        Array.Empty<ReferenceMappingDto>();
}
