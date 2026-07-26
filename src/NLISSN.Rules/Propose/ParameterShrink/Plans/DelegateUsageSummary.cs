namespace Deletion.Rules;

public sealed record DelegateUsageSummary(
  IReadOnlyList<MethodRewrite> MethodRewrites,
  IReadOnlyList<LocalFunctionRewrite> LocalFunctionRewrites,
  IReadOnlyList<ExpressionRewrite> LambdaRewrites,
  IReadOnlyList<InvocationRewrite> InvocationRewrites,
  IReadOnlyCollection<string> MethodGroupTargets);
