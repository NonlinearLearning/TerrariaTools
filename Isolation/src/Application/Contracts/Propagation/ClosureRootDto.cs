using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 闭包根 DTO。
/// </summary>
public sealed class ClosureRootDto
{
    public string ClassName { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;
}
