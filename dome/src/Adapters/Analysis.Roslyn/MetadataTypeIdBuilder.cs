using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

public static class MetadataTypeIdBuilder
{
    public static string Build(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";
    }
}

