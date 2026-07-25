using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record DelegateComplexShrinkPlan(
  DelegateDeclarationSyntax DelegateDeclaration,
  DelegateDeclarationSyntax ReplacementDelegate,
  IReadOnlyList<MethodRewrite> MethodRewrites,
  IReadOnlyList<LocalFunctionRewrite> LocalFunctionRewrites,
  IReadOnlyList<ExpressionRewrite> LambdaRewrites,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
