using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 任意表达式锚点及其语义等价的替换语法对。
public sealed record ExpressionRewrite(
  ExpressionSyntax Expression,
  ExpressionSyntax Replacement);
