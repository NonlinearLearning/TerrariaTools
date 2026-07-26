using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record ElementAccessRewrite(
  ElementAccessExpressionSyntax ElementAccess,
  ElementAccessExpressionSyntax Replacement);
