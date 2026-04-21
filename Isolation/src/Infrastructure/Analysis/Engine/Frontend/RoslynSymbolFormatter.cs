using Domain.Analysis.Engine.Model;
using Logic.Analysis.Engine.Frontend;
using Microsoft.CodeAnalysis;

namespace Infrastructure.Analysis.Engine.Frontend;

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
        return SymbolFormattingRules.NormalizeTypeFullName(symbol?.ToDisplayString(TypeDisplayFormat));
    }

    /// <summary>
    /// 生成稳定的方法全名。
    /// </summary>
    public static string GetMethodFullName(IMethodSymbol? symbol)
    {
        if (symbol is null)
        {
            return FrontendGraphConventions.Unknown;
        }

        return SymbolFormattingRules.BuildMethodFullName(
            GetTypeFullName(symbol.ContainingType),
            symbol.Name,
            symbol.Parameters.Select(parameter => GetTypeFullName(parameter.Type)));
    }

    /// <summary>
    /// 生成稳定的方法签名。
    /// </summary>
    public static string GetMethodSignature(IMethodSymbol? symbol)
    {
        if (symbol is null)
        {
            return FrontendGraphConventions.Unknown;
        }

        return SymbolFormattingRules.BuildMethodSignature(
            GetTypeFullName(symbol.ReturnType),
            symbol.Parameters.Select(parameter => GetTypeFullName(parameter.Type)));
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
        SymbolIdentityDescriptor descriptor = symbol switch
        {
            IMethodSymbol methodSymbol => new(
                SymbolIdentityKind.Method,
                methodSymbol.Name,
                GetTypeFullName(methodSymbol.ContainingType),
                methodSymbol.Parameters.Select(parameter => GetTypeFullName(parameter.Type)).ToArray(),
                locationPart,
                symbol.Kind.ToString(),
                symbol.ToDisplayString()),
            IParameterSymbol parameterSymbol => new(
                SymbolIdentityKind.Parameter,
                parameterSymbol.Name,
                GetMethodFullName(parameterSymbol.ContainingSymbol as IMethodSymbol),
                Array.Empty<string>(),
                locationPart,
                symbol.Kind.ToString(),
                symbol.ToDisplayString()),
            ILocalSymbol localSymbol => new(
                SymbolIdentityKind.Local,
                localSymbol.Name,
                localSymbol.ContainingSymbol.ToDisplayString(),
                Array.Empty<string>(),
                locationPart,
                symbol.Kind.ToString(),
                symbol.ToDisplayString()),
            IFieldSymbol fieldSymbol => new(
                SymbolIdentityKind.Field,
                fieldSymbol.Name,
                GetTypeFullName(fieldSymbol.ContainingType),
                Array.Empty<string>(),
                locationPart,
                symbol.Kind.ToString(),
                symbol.ToDisplayString()),
            IPropertySymbol propertySymbol => new(
                SymbolIdentityKind.Property,
                propertySymbol.Name,
                GetTypeFullName(propertySymbol.ContainingType),
                Array.Empty<string>(),
                locationPart,
                symbol.Kind.ToString(),
                symbol.ToDisplayString()),
            INamedTypeSymbol namedTypeSymbol => new(
                SymbolIdentityKind.NamedType,
                GetTypeFullName(namedTypeSymbol),
                null,
                Array.Empty<string>(),
                locationPart,
                symbol.Kind.ToString(),
                symbol.ToDisplayString()),
            _ => new(
                SymbolIdentityKind.Unknown,
                symbol.Name,
                null,
                Array.Empty<string>(),
                locationPart,
                symbol.Kind.ToString(),
                symbol.ToDisplayString()),
        };

        return SymbolFormattingRules.BuildSymbolId(descriptor);
    }

    /// <summary>
    /// 为类型生成领域层类型标识。
    /// </summary>
    public static TypeId GetTypeId(ITypeSymbol? symbol) => SymbolFormattingRules.BuildTypeId(GetTypeFullName(symbol));
}
