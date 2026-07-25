using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record LocalFunctionRewrite(
  LocalFunctionStatementSyntax LocalFunction,
  LocalFunctionStatementSyntax ReplacementLocalFunction);
