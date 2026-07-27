using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 局部函数参数删除及其同一语义作用域内调用点的替换计划。
public sealed record LocalFunctionParameterShrinkPlan(
  LocalFunctionStatementSyntax LocalFunction,
  LocalFunctionStatementSyntax ReplacementLocalFunction,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
