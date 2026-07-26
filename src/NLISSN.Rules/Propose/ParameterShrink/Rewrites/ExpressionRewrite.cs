using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record ExpressionRewrite(
  ExpressionSyntax Expression,
  ExpressionSyntax Replacement);
