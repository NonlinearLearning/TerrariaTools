using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record PublicMethodParameterShrinkPlan(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
