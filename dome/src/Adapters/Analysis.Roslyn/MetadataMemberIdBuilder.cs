using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

public static class MetadataMemberIdBuilder
{
    public static ModelPrimitives.MemberId Build(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method when method.MethodKind == MethodKind.PropertyGet =>
                new ModelPrimitives.MemberId($"{BuildTypeName(method.ContainingType)}.{method.AssociatedSymbol?.Name}.get"),
            IMethodSymbol method when method.MethodKind == MethodKind.PropertySet =>
                new ModelPrimitives.MemberId($"{BuildTypeName(method.ContainingType)}.{method.AssociatedSymbol?.Name}.set"),
            IMethodSymbol method when method.MethodKind == MethodKind.Constructor =>
                new ModelPrimitives.MemberId($"{BuildTypeName(method.ContainingType)}..ctor({BuildParameterList(method.Parameters)})"),
            IMethodSymbol method =>
                new ModelPrimitives.MemberId($"{BuildTypeName(method.ContainingType)}.{method.Name}({BuildParameterList(method.Parameters)})"),
            IFieldSymbol field =>
                new ModelPrimitives.MemberId($"{BuildTypeName(field.ContainingType)}.{field.Name}"),
            IPropertySymbol property =>
                new ModelPrimitives.MemberId($"{BuildTypeName(property.ContainingType)}.{property.Name}"),
            _ => new ModelPrimitives.MemberId(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
        };
    }

    private static string BuildTypeName(INamedTypeSymbol? typeSymbol) =>
        typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";

    private static string BuildParameterList(ImmutableArray<IParameterSymbol> parameters) =>
        string.Join(", ", parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
}



