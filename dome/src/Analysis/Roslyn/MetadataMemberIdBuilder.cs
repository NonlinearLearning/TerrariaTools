using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

public static class MetadataMemberIdBuilder
{
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

    private static string BuildTypeName(INamedTypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";
    }

    private static string BuildParameterList(ImmutableArray<IParameterSymbol> parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }
}
