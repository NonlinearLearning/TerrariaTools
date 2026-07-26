using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record LocalFunctionRewrite(
  LocalFunctionStatementSyntax LocalFunction,
  LocalFunctionStatementSyntax ReplacementLocalFunction);
