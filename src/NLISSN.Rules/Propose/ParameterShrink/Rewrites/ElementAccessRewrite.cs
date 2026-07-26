using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record ElementAccessRewrite(
  ElementAccessExpressionSyntax ElementAccess,
  ElementAccessExpressionSyntax Replacement);
