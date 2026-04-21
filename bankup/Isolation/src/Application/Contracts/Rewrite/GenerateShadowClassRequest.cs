namespace Application.Contracts.Rewrite;

/// <summary>
/// 生成影子类请求。
/// </summary>
public sealed class GenerateShadowClassRequest
{
    public string SourceCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public int? ParameterCount { get; set; }
}
