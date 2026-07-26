using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record DelegateParameterShrinkPlan(
  DelegateDeclarationSyntax DelegateDeclaration,
  DelegateDeclarationSyntax ReplacementDelegate);
