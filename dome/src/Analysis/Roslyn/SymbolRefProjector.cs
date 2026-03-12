using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

internal static class SymbolRefProjector
{
    public static SymbolRef? Project(ISymbol? symbol, MemberId declaringMemberId)
    {
        if (symbol == null)
        {
            return null;
        }

        var declarationSyntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var spanStart = declarationSyntax?.SpanStart ?? -1;
        var spanLength = declarationSyntax?.Span.Length ?? 0;

        return new SymbolRef(
            BuildSymbolKey(symbol, declaringMemberId),
            symbol.Name,
            MapKind(symbol),
            declaringMemberId,
            spanStart,
            spanLength);
    }

    public static SymbolRef? ProjectDeclared(LocalDeclarationStatementSyntax statement, VariableDeclaratorSyntax variable, SemanticModel model, MemberId declaringMemberId)
    {
        var symbol = model.GetDeclaredSymbol(variable);
        return Project(symbol, declaringMemberId);
    }

    public static SymbolRef? ProjectUsed(IdentifierNameSyntax identifier, SemanticModel model, MemberId declaringMemberId)
    {
        var symbol = model.GetSymbolInfo(identifier).Symbol;
        return Project(symbol, declaringMemberId);
    }

    private static string BuildSymbolKey(ISymbol symbol, MemberId declaringMemberId)
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

    private static SymbolKindRef MapKind(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol => SymbolKindRef.Local,
            IParameterSymbol => SymbolKindRef.Parameter,
            IFieldSymbol => SymbolKindRef.Field,
            IPropertySymbol => SymbolKindRef.Property,
            _ => SymbolKindRef.Unknown
        };
    }
}
