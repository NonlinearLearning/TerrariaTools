namespace Application.Contracts.Rewrite;

/// <summary>
/// 删除方法请求。
/// </summary>
public sealed class DeleteMethodRequest
{
    public string SourceCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public int? ParameterCount { get; set; }
}
