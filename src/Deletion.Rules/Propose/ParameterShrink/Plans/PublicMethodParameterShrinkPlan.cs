using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record PublicMethodParameterShrinkPlan(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
