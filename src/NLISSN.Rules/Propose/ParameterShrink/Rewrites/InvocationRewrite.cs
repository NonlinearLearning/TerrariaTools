using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 调用表达式及其移除对应实参后的替换语法对。
public sealed record InvocationRewrite(
  InvocationExpressionSyntax Invocation,
  InvocationExpressionSyntax Replacement);
