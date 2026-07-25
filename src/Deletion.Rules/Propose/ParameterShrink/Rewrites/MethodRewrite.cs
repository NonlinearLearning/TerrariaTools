using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record MethodRewrite(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod);
