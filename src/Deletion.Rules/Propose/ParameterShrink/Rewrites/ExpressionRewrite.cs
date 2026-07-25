using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record ExpressionRewrite(
  ExpressionSyntax Expression,
  ExpressionSyntax Replacement);
