namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 表示参与符号标识生成的稳定描述信息。
/// </summary>
public sealed record SymbolIdentityDescriptor(
    SymbolIdentityKind Kind,
    string Name,
    string? ContainerDisplay = null,
    IReadOnlyCollection<string>? ParameterTypeFullNames = null,
    string? LocationPart = null,
    string? FallbackKind = null,
    string? FallbackDisplay = null)
{
    /// <summary>
    /// 获取参数类型全名集合。
    /// </summary>
    public IReadOnlyCollection<string> ParameterTypeFullNames { get; init; } =
        ParameterTypeFullNames ?? Array.Empty<string>();
}
