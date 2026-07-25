using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record LocalFunctionParameterShrinkPlan(
  LocalFunctionStatementSyntax LocalFunction,
  LocalFunctionStatementSyntax ReplacementLocalFunction,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
