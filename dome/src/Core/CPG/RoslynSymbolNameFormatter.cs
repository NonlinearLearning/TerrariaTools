using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Core.Cpg;

public static class RoslynSymbolNameFormatter
{
    public static string? GetTypeFullName(ITypeSymbol? symbol, string? fallback = null)
    {
        return GetFullName(symbol) ?? fallback;
    }

    public static string? GetFullName(ITypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return null;
        }

        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    public static string? GetFullName(IMethodSymbol? symbol)
    {
        if (symbol is null)
        {
            return null;
        }

        string? containingTypeFullName = GetFullName(symbol.ContainingType);
        return string.IsNullOrWhiteSpace(containingTypeFullName)
            ? symbol.Name
            : $"{containingTypeFullName}.{symbol.Name}";
    }
}
