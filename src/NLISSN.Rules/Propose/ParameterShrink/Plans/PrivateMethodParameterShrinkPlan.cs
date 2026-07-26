using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record PrivateMethodParameterShrinkPlan(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
