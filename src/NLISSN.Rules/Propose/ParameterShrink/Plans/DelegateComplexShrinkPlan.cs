using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 委托参数删除的完整同步改写计划，覆盖签名、绑定表达式与调用链。
public sealed record DelegateComplexShrinkPlan(
  DelegateDeclarationSyntax DelegateDeclaration,
  DelegateDeclarationSyntax ReplacementDelegate,
  IReadOnlyList<MethodRewrite> MethodRewrites,
  IReadOnlyList<LocalFunctionRewrite> LocalFunctionRewrites,
  IReadOnlyList<ExpressionRewrite> LambdaRewrites,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
