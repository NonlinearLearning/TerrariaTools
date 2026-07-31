using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace NLISSN.Rules;

/// 集中定义参数收缩允许改变的方法形状，排除可能发生继承或重载重绑定的成员。
public static class MethodProposalSafety
{
    // 判断私有普通方法是否满足参数收缩的安全前提，避免继承和接口绑定漂移。
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

    // 判断扩展方法是否只会影响非接收者参数收缩，而不会破坏接收者绑定。
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

    // 判断非私有方法是否仍处在可保守收缩的范围内。
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
