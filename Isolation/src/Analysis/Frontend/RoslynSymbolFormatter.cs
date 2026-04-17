using Analysis.Model;
using Microsoft.CodeAnalysis;

namespace Analysis.Frontend;

/// <summary>
/// 负责把 Roslyn 符号转换成分析层使用的稳定名字。
///
/// 阶段二里很多 pass 都依赖稳定全名。
/// 如果每个地方各自拼字符串，调用图和类型关系很快会不一致。
/// </summary>
internal static class RoslynSymbolFormatter
{
    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions:
            SymbolDisplayGenericsOptions.IncludeTypeParameters |
            SymbolDisplayGenericsOptions.IncludeVariance,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>
    /// 生成稳定的类型全名。
    /// </summary>
    public static string GetTypeFullName(ITypeSymbol? symbol)
    {
        return symbol is null
            ? "<unknown>"
            : Normalize(symbol.ToDisplayString(TypeDisplayFormat));
    }

    /// <summary>
    /// 生成稳定的方法全名。
    /// </summary>
    public static string GetMethodFullName(IMethodSymbol? symbol)
    {
        if (symbol is null)
        {
            return "<unknown>";
        }

        string containingType = GetTypeFullName(symbol.ContainingType);
        string parameters = string.Join(", ", symbol.Parameters.Select(parameter => GetTypeFullName(parameter.Type)));
        return $"{containingType}.{symbol.Name}({parameters})";
    }

    /// <summary>
    /// 生成稳定的方法签名。
    /// </summary>
    public static string GetMethodSignature(IMethodSymbol? symbol)
    {
        if (symbol is null)
        {
            return "<unknown>";
        }

        string parameters = string.Join(", ", symbol.Parameters.Select(parameter => GetTypeFullName(parameter.Type)));
        return $"{GetTypeFullName(symbol.ReturnType)} ({parameters})";
    }

    /// <summary>
    /// 为 Roslyn 符号生成稳定标识。
    /// </summary>
    public static SymbolId? GetSymbolId(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return null;
        }

        string locationPart = symbol.Locations.FirstOrDefault()?.SourceSpan.Start.ToString() ?? "metadata";
        string value = symbol switch
        {
            IMethodSymbol methodSymbol => $"method:{GetMethodFullName(methodSymbol)}",
            IParameterSymbol parameterSymbol => $"parameter:{GetMethodFullName(parameterSymbol.ContainingSymbol as IMethodSymbol)}:{parameterSymbol.Name}",
            ILocalSymbol localSymbol => $"local:{localSymbol.ContainingSymbol.ToDisplayString()}:{localSymbol.Name}:{locationPart}",
            IFieldSymbol fieldSymbol => $"field:{GetTypeFullName(fieldSymbol.ContainingType)}.{fieldSymbol.Name}",
            IPropertySymbol propertySymbol => $"property:{GetTypeFullName(propertySymbol.ContainingType)}.{propertySymbol.Name}",
            INamedTypeSymbol namedTypeSymbol => $"type:{GetTypeFullName(namedTypeSymbol)}",
            _ => $"{symbol.Kind}:{symbol.ToDisplayString()}:{locationPart}",
        };

        return new SymbolId(value);
    }

    /// <summary>
    /// 为类型生成领域层类型标识。
    /// </summary>
    public static TypeId GetTypeId(ITypeSymbol? symbol) => new(GetTypeFullName(symbol));

    private static string Normalize(string value)
    {
        return value.Replace("global::", string.Empty, StringComparison.Ordinal);
    }
}
