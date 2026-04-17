namespace Application.Contracts.Rewrite;

/// <summary>
/// 成员切片 DTO。
/// </summary>
public sealed class MemberSliceDto
{
    public string ClassName { get; set; } = string.Empty;

    public string RootMemberName { get; set; } = string.Empty;

    public string SourceCode { get; set; } = string.Empty;

    public IReadOnlyCollection<string> MemberNames { get; set; } = Array.Empty<string>();
}
