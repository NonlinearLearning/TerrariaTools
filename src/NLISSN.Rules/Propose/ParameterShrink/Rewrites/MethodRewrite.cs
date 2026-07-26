using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record MethodRewrite(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod);
