using Domain.Analysis.Engine.Model;

namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 提供前端符号命名和标识生成的纯规则。
/// </summary>
public static class SymbolFormattingRules
{
    /// <summary>
    /// 规范化类型全名。
    /// </summary>
    public static string NormalizeTypeFullName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "<unknown>"
            : value.Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// 生成稳定的方法全名。
    /// </summary>
    public static string BuildMethodFullName(
        string? containingTypeFullName,
        string? methodName,
        IEnumerable<string> parameterTypeFullNames)
    {
        ArgumentNullException.ThrowIfNull(parameterTypeFullNames);

        string containingType = NormalizeTypeFullName(containingTypeFullName);
        string name = string.IsNullOrWhiteSpace(methodName) ? "<unknown>" : methodName;
        string parameters = string.Join(", ", parameterTypeFullNames.Select(NormalizeTypeFullName));
        return $"{containingType}.{name}({parameters})";
    }

    /// <summary>
    /// 生成稳定的方法签名。
    /// </summary>
    public static string BuildMethodSignature(
        string? returnTypeFullName,
        IEnumerable<string> parameterTypeFullNames)
    {
        ArgumentNullException.ThrowIfNull(parameterTypeFullNames);

        string parameters = string.Join(", ", parameterTypeFullNames.Select(NormalizeTypeFullName));
        return $"{NormalizeTypeFullName(returnTypeFullName)} ({parameters})";
    }

    /// <summary>
    /// 生成稳定符号标识。
    /// </summary>
    public static SymbolId BuildSymbolId(SymbolIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        string locationPart = string.IsNullOrWhiteSpace(descriptor.LocationPart)
            ? "metadata"
            : descriptor.LocationPart;

        string value = descriptor.Kind switch
        {
            SymbolIdentityKind.Method => $"method:{BuildMethodFullName(descriptor.ContainerDisplay, descriptor.Name, descriptor.ParameterTypeFullNames)}",
            SymbolIdentityKind.Parameter => $"parameter:{descriptor.ContainerDisplay}:{descriptor.Name}",
            SymbolIdentityKind.Local => $"local:{descriptor.ContainerDisplay}:{descriptor.Name}:{locationPart}",
            SymbolIdentityKind.Field => $"field:{NormalizeTypeFullName(descriptor.ContainerDisplay)}.{descriptor.Name}",
            SymbolIdentityKind.Property => $"property:{NormalizeTypeFullName(descriptor.ContainerDisplay)}.{descriptor.Name}",
            SymbolIdentityKind.NamedType => $"type:{NormalizeTypeFullName(descriptor.Name)}",
            _ => $"{descriptor.FallbackKind}:{descriptor.FallbackDisplay}:{locationPart}",
        };

        return new SymbolId(value);
    }

    /// <summary>
    /// 基于类型全名生成领域层类型标识。
    /// </summary>
    public static TypeId BuildTypeId(string? typeFullName) => new(NormalizeTypeFullName(typeFullName));
}
