using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record PrivateMethodParameterShrinkPlan(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
