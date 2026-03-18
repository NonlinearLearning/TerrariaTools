using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Analysis.Legacy;

internal static class MetadataTypeIdBuilder
{
    public static string Build(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";
    }
}
