namespace NLISSN.Rules;

/// 委托收缩前收集的已验证绑定使用摘要，供多个提案规则共享。
public sealed record DelegateUsageSummary(
  IReadOnlyList<MethodRewrite> MethodRewrites,
  IReadOnlyList<LocalFunctionRewrite> LocalFunctionRewrites,
  IReadOnlyList<ExpressionRewrite> LambdaRewrites,
  IReadOnlyList<InvocationRewrite> InvocationRewrites,
  IReadOnlyCollection<string> MethodGroupTargets);
