namespace Application.Contracts.Rewrite;

/// <summary>
/// 清空方法体请求。
/// </summary>
public sealed class ClearMethodBodyRequest
{
    public string SourceCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public int? ParameterCount { get; set; }
}
