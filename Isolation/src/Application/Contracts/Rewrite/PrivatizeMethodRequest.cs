namespace Application.Contracts.Rewrite;

/// <summary>
/// 方法私有化请求。
/// </summary>
public sealed class PrivatizeMethodRequest
{
    public string SourceCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public int? ParameterCount { get; set; }
}
