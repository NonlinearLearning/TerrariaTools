using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

public static class CommonConditions
{
    public static bool IsAssignment(ExpressionSyntax expression)
    {
        return expression is AssignmentExpressionSyntax;
    }

    public static bool HasAttribute(MemberDeclarationSyntax node, string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return false;
        }

        var expected = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName
            : $"{attributeName}Attribute";

        foreach (var attr in node.AttributeLists.SelectMany(list => list.Attributes))
        {
            var raw = attr.Name.ToString();
            if (string.Equals(raw, attributeName, StringComparison.Ordinal)
                || string.Equals(raw, expected, StringComparison.Ordinal)
                || raw.EndsWith($".{attributeName}", StringComparison.Ordinal)
                || raw.EndsWith($".{expected}", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPublic(MemberDeclarationSyntax node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax type => type.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)),
            BaseMethodDeclarationSyntax method => method.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)),
            BasePropertyDeclarationSyntax property => property.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)),
            BaseFieldDeclarationSyntax field => field.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)),
            DelegateDeclarationSyntax del => del.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)),
            _ => false
        };
    }

    public static bool Implements(TypeDeclarationSyntax node, string interfaceName, IRewriteContext context)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return false;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(node) as INamedTypeSymbol;
        if (symbol is null)
        {
            return false;
        }

        return symbol.AllInterfaces.Any(i =>
            string.Equals(i.Name, interfaceName, StringComparison.Ordinal)
            || string.Equals(i.ToDisplayString(), interfaceName, StringComparison.Ordinal));
    }
}
