using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace RoslynPrototype.Decision;

public static class DeleteClassMethodProposalSafety
{
    public static bool IsSafePrivateMethod(MethodDeclarationSyntax method)
    {
        if (method.ExplicitInterfaceSpecifier is not null)
        {
            return false;
        }

        return method.Modifiers.Any(token => token.IsKind(SyntaxKind.PrivateKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.ProtectedKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.InternalKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.OverrideKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.AbstractKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.VirtualKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.ExternKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword));
    }

    public static bool IsSafeExtensionReceiverMethod(MethodDeclarationSyntax method)
    {
        if (method.ExplicitInterfaceSpecifier is not null)
        {
            return false;
        }

        return method.Modifiers.Any(token => token.IsKind(SyntaxKind.StaticKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.OverrideKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.AbstractKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.VirtualKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.ExternKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword));
    }

    public static bool IsSafeNonPrivateMethod(MethodDeclarationSyntax method)
    {
        if (method.ExplicitInterfaceSpecifier is not null)
        {
            return false;
        }

        return !method.Modifiers.Any(token => token.IsKind(SyntaxKind.PrivateKeyword)) &&
          (method.Modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword)) ||
           method.Modifiers.Any(token => token.IsKind(SyntaxKind.ProtectedKeyword)) ||
           method.Modifiers.Any(token => token.IsKind(SyntaxKind.InternalKeyword))) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.OverrideKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.AbstractKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.VirtualKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.ExternKeyword)) &&
          !method.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword));
    }
}
