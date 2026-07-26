using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record LocalFunctionParameterShrinkPlan(
  LocalFunctionStatementSyntax LocalFunction,
  LocalFunctionStatementSyntax ReplacementLocalFunction,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
