namespace Application.Contracts.Rewrite;

/// <summary>
/// 提取最小运行闭包请求。
/// </summary>
public sealed class ExtractMinimalRuntimeClosureRequest
{
    public string SourceCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public int? ParameterCount { get; set; }
}
