namespace Application.Contracts.Rewrite;

/// <summary>
/// 构建成员切片请求。
/// </summary>
public sealed class BuildMemberSliceRequest
{
    public string SourceCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public int? ParameterCount { get; set; }
}
