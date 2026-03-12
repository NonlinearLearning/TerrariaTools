using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 元数据成员ID构建器，用于为符号构建唯一的成员标识符。
/// </summary>
public static class MetadataMemberIdBuilder
{
    /// <summary>
    /// 根据给定的符号构建成员ID。
    /// </summary>
    /// <param name="symbol">要构建ID的符号。</param>
    /// <returns>构建的成员ID。</returns>
    public static MemberId Build(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method when method.MethodKind == MethodKind.PropertyGet =>
                new MemberId($"{BuildTypeName(method.ContainingType)}.{method.AssociatedSymbol?.Name}.get"),
            IMethodSymbol method when method.MethodKind == MethodKind.PropertySet =>
                new MemberId($"{BuildTypeName(method.ContainingType)}.{method.AssociatedSymbol?.Name}.set"),
            IMethodSymbol method when method.MethodKind == MethodKind.Constructor =>
                new MemberId($"{BuildTypeName(method.ContainingType)}..ctor({BuildParameterList(method.Parameters)})"),
            IMethodSymbol method =>
                new MemberId($"{BuildTypeName(method.ContainingType)}.{method.Name}({BuildParameterList(method.Parameters)})"),
            IFieldSymbol field =>
                new MemberId($"{BuildTypeName(field.ContainingType)}.{field.Name}"),
            IPropertySymbol property =>
                new MemberId($"{BuildTypeName(property.ContainingType)}.{property.Name}"),
            _ => new MemberId(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
        };
    }

    /// <summary>
    /// 构建类型名称。
    /// </summary>
    /// <param name="typeSymbol">类型符号。</param>
    /// <returns>类型名称字符串。</returns>
    private static string BuildTypeName(INamedTypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";
    }

    /// <summary>
    /// 构建参数列表字符串。
    /// </summary>
    /// <param name="parameters">参数符号数组。</param>
    /// <returns>参数列表字符串。</returns>
    private static string BuildParameterList(ImmutableArray<IParameterSymbol> parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }
}
