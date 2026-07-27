using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 元素访问及其移除对应索引实参后的替换语法对。
public sealed record ElementAccessRewrite(
  ElementAccessExpressionSyntax ElementAccess,
  ElementAccessExpressionSyntax Replacement);
