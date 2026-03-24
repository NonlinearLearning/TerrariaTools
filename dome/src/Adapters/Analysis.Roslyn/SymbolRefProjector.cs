using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

internal static class SymbolRefProjector
{
    public static ModelAnalysis.SymbolRef? Project(ISymbol? symbol, ModelPrimitives.MemberId declaringMemberId)
    {
        if (symbol == null)
        {
            return null;
        }

        var declarationSyntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var spanStart = declarationSyntax?.SpanStart ?? -1;
        var spanLength = declarationSyntax?.Span.Length ?? 0;

        return new ModelAnalysis.SymbolRef(
            BuildSymbolKey(symbol, declaringMemberId),
            symbol.Name,
            MapKind(symbol),
            declaringMemberId,
            spanStart,
            spanLength);
    }

    public static ModelAnalysis.SymbolRef? ProjectDeclared(
        LocalDeclarationStatementSyntax statement,
        VariableDeclaratorSyntax variable,
        SemanticModel model,
        ModelPrimitives.MemberId declaringMemberId)
    {
        var symbol = model.GetDeclaredSymbol(variable);
        return Project(symbol, declaringMemberId);
    }

    public static ModelAnalysis.SymbolRef? ProjectUsed(
        IdentifierNameSyntax identifier,
        SemanticModel model,
        ModelPrimitives.MemberId declaringMemberId)
    {
        var symbol = model.GetSymbolInfo(identifier).Symbol;
        return Project(symbol, declaringMemberId);
    }

    private static string BuildSymbolKey(ISymbol symbol, ModelPrimitives.MemberId declaringMemberId)
    {
        if (symbol is ILocalSymbol or IParameterSymbol)
        {
            var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var spanStart = syntax?.SpanStart ?? -1;
            var spanLength = syntax?.Span.Length ?? 0;
            return $"{declaringMemberId.Value}|{MapKind(symbol)}|{symbol.Name}|{spanStart}|{spanLength}";
        }

        return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static ModelAnalysis.SymbolKindRef MapKind(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol => ModelAnalysis.SymbolKindRef.Local,
            IParameterSymbol => ModelAnalysis.SymbolKindRef.Parameter,
            IFieldSymbol => ModelAnalysis.SymbolKindRef.Field,
            IPropertySymbol => ModelAnalysis.SymbolKindRef.Property,
            _ => ModelAnalysis.SymbolKindRef.Unknown
        };
    }
}



