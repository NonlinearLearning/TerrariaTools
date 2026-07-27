using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 无外部绑定改写需求的简单委托参数删除计划。
public sealed record DelegateParameterShrinkPlan(
  DelegateDeclarationSyntax DelegateDeclaration,
  DelegateDeclarationSyntax ReplacementDelegate);
