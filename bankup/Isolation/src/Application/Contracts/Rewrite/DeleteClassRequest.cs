namespace Application.Contracts.Rewrite;

/// <summary>
/// 删除类型请求。
/// </summary>
public sealed class DeleteClassRequest
{
    public string SourceCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;
}
