using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record InvocationRewrite(
  InvocationExpressionSyntax Invocation,
  InvocationExpressionSyntax Replacement);
