using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 方法参数删除及其已验证调用点替换的原子计划。
public sealed record MethodParameterShrinkPlan(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
