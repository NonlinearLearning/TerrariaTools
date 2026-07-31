using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Rules;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NLISSN.Rules;

/// 先证明声明与所有受影响调用点可同步改写，再生成参数收缩计划。
/// 无法覆盖的调用形状、重载冲突或语义绑定不稳定时必须返回 false。
public sealed class ParameterShrinkAnalyzer
{
    // 在所有命名参数调用都可安全删除目标实参时，生成私有方法的命名参数收缩计划。
    public bool TryBuildNamedArgumentMethodPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out MethodParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveSupportedMethodParameter(
              context,
              typeSyntax,
              out var method,
              out var methodSymbol,
              out var parameter,
              out var parameterSymbol,
              out var parameterIndex) ||
            HasUnsupportedParameterShape(parameter, parameterSymbol) ||
            HasConflictingReplacementOverload(methodSymbol, parameterIndex) ||
            !TryBuildReplacementMethod(method, parameter, out var replacementMethod) ||
            !TryCollectNamedArgumentInvocationRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterSymbol,
              out var invocationRewrites))
        {
            return false;
        }

        plan = new MethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    // 在可选参数既能删声明又不破坏省略调用语义时，生成方法收缩计划。
    public bool TryBuildOptionalParameterMethodPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out MethodParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveSupportedMethodParameter(
              context,
              typeSyntax,
              out var method,
              out var methodSymbol,
              out var parameter,
              out var parameterSymbol,
              out var parameterIndex) ||
            !IsOptionalParameter(parameter, parameterSymbol) ||
            HasUnsupportedOptionalParameterShape(parameter, parameterSymbol) ||
            HasConflictingReplacementOverload(methodSymbol, parameterIndex) ||
            !TryBuildReplacementMethod(method, parameter, out var replacementMethod) ||
            !TryCollectOptionalInvocationRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterSymbol,
              requireCallsites: MethodProposalSafety.IsSafeNonPrivateMethod(method),
              out var invocationRewrites))
        {
            return false;
        }

        plan = new MethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    // 仅在 params 槽位始终被省略且不存在重载冲突时，生成方法收缩计划。
    public bool TryBuildParamsMethodPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out MethodParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveSupportedMethodParameter(
              context,
              typeSyntax,
              out var method,
              out var methodSymbol,
              out var parameter,
              out var parameterSymbol,
              out var parameterIndex) ||
            !IsParamsParameter(parameter, parameterSymbol) ||
            parameterIndex != method.ParameterList.Parameters.Count - 1 ||
            HasConflictingReplacementOverload(methodSymbol, parameterIndex) ||
            !TryBuildReplacementMethod(method, parameter, out var replacementMethod) ||
            !TryCollectParamsInvocationRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterSymbol,
              requireCallsites: MethodProposalSafety.IsSafeNonPrivateMethod(method),
              out var invocationRewrites))
        {
            return false;
        }

        plan = new MethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    // 为普通私有方法生成参数收缩计划；调用点可为空，因为私有删除链路允许只改声明。
    public bool TryBuildPrivateMethodPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out MethodParameterShrinkPlan plan)
    {
        return TryBuildMethodPlan(
          context,
          typeSyntax,
          MethodProposalSafety.IsSafePrivateMethod,
          requireCallsites: false,
          out plan);
    }

    // 为非私有方法生成参数收缩计划，并要求调用点必须被完整覆盖。
    public bool TryBuildPublicMethodPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out MethodParameterShrinkPlan plan)
    {
        return TryBuildMethodPlan(
          context,
          typeSyntax,
          MethodProposalSafety.IsSafeNonPrivateMethod,
          requireCallsites: true,
          out plan);
    }

    // 为普通位置参数局部函数生成声明与调用点同步收缩计划。
    public bool TryBuildLocalFunctionPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out LocalFunctionParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveLocalFunctionParameter(typeSyntax, out var localFunction, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(localFunction, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementLocalFunction(localFunction, parameter, out var replacementLocalFunction) ||
            !TryCollectInvocationRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterIndex,
              localFunction.ParameterList.Parameters.Count,
              requireCallsites: true,
              out var invocationRewrites))
        {
            return false;
        }

        plan = new LocalFunctionParameterShrinkPlan(
          localFunction,
          replacementLocalFunction,
          invocationRewrites);
        return true;
    }

    // 为命名参数局部函数调用生成声明与调用点同步收缩计划。
    public bool TryBuildNamedArgumentLocalFunctionPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out LocalFunctionParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveLocalFunctionParameter(typeSyntax, out var localFunction, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(localFunction, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementLocalFunction(localFunction, parameter, out var replacementLocalFunction) ||
            !TryCollectNamedArgumentInvocationRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              methodSymbol,
              methodSymbol.Parameters[parameterIndex],
              out var invocationRewrites))
        {
            return false;
        }

        plan = new LocalFunctionParameterShrinkPlan(
          localFunction,
          replacementLocalFunction,
          invocationRewrites);
        return true;
    }

    // 为带默认值的局部函数参数生成可保守执行的收缩计划。
    public bool TryBuildOptionalParameterLocalFunctionPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out LocalFunctionParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveLocalFunctionParameter(typeSyntax, out var localFunction, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(localFunction, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            !IsOptionalParameter(parameter, methodSymbol.Parameters[parameterIndex]) ||
            HasUnsupportedOptionalParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementLocalFunction(localFunction, parameter, out var replacementLocalFunction) ||
            !TryCollectOptionalInvocationRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              methodSymbol,
              methodSymbol.Parameters[parameterIndex],
              requireCallsites: true,
              out var invocationRewrites))
        {
            return false;
        }

        plan = new LocalFunctionParameterShrinkPlan(
          localFunction,
          replacementLocalFunction,
          invocationRewrites);
        return true;
    }

    // 为普通位置索引器参数生成声明与访问点同步收缩计划。
    public bool TryBuildIndexerPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out IndexerParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveIndexerParameter(typeSyntax, out var indexer, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(indexer, CancellationToken.None) is not IPropertySymbol indexerSymbol ||
            parameterIndex >= indexerSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, indexerSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementIndexer(indexer, parameter, out var replacementIndexer) ||
            !TryCollectElementAccessRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              indexerSymbol,
              parameterIndex,
              indexer.ParameterList.Parameters.Count,
              requireCallsites: true,
              out var accessRewrites))
        {
            return false;
        }

        plan = new IndexerParameterShrinkPlan(indexer, replacementIndexer, accessRewrites);
        return true;
    }

    // 为命名索引参数访问生成声明与访问点同步收缩计划。
    public bool TryBuildNamedArgumentIndexerPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out IndexerParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveIndexerParameter(typeSyntax, out var indexer, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(indexer, CancellationToken.None) is not IPropertySymbol indexerSymbol ||
            parameterIndex >= indexerSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, indexerSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementIndexer(indexer, parameter, out var replacementIndexer) ||
            !TryCollectNamedElementAccessRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              indexerSymbol,
              indexerSymbol.Parameters[parameterIndex],
              out var accessRewrites))
        {
            return false;
        }

        plan = new IndexerParameterShrinkPlan(indexer, replacementIndexer, accessRewrites);
        return true;
    }

    // 仅在委托没有额外外部绑定时，生成只改签名的简单委托收缩计划。
    public bool TryBuildDelegatePlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out DelegateParameterShrinkPlan plan)
    {
        plan = null!;
        var invokeMethod = default(IMethodSymbol);

        if (!TryResolveDelegateParameter(typeSyntax, out var delegateDeclaration, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(delegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
            (invokeMethod = delegateSymbol.DelegateInvokeMethod) is null ||
            parameterIndex >= invokeMethod.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, invokeMethod.Parameters[parameterIndex]) ||
            !TryBuildReplacementDelegate(delegateDeclaration, parameter, out var replacementDelegate) ||
            HasDelegateReferences(context.Runtime, context.SemanticModel.Compilation, delegateSymbol))
        {
            return false;
        }

        plan = new DelegateParameterShrinkPlan(delegateDeclaration, replacementDelegate);
        return true;
    }

    // 当委托只通过 method group 绑定传播时，生成声明与绑定同步收缩计划。
    public bool TryBuildDelegateMethodGroupPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out DelegateComplexShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveDelegateParameter(typeSyntax, out var delegateDeclaration, out _, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(delegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
            delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
            parameterIndex >= invokeMethod.Parameters.Length ||
            !TryBuildReplacementDelegate(
              delegateDeclaration,
              delegateDeclaration.ParameterList.Parameters[parameterIndex],
              out var replacementDelegate) ||
            !TryCollectDelegateUsageSummary(
              context,
              delegateSymbol,
              invokeMethod.Parameters[parameterIndex],
              parameterIndex,
              out var usageSummary) ||
            usageSummary.MethodGroupTargets.Count == 0 ||
            usageSummary.LambdaRewrites.Count > 0)
        {
            return false;
        }

        plan = new DelegateComplexShrinkPlan(
          delegateDeclaration,
          replacementDelegate,
          usageSummary);
        return true;
    }

    // 当委托只通过 lambda 绑定传播时，生成声明与绑定同步收缩计划。
    public bool TryBuildDelegateLambdaPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out DelegateComplexShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveDelegateParameter(typeSyntax, out var delegateDeclaration, out _, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(delegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
            delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
            parameterIndex >= invokeMethod.Parameters.Length ||
            !TryBuildReplacementDelegate(
              delegateDeclaration,
              delegateDeclaration.ParameterList.Parameters[parameterIndex],
              out var replacementDelegate) ||
            !TryCollectDelegateUsageSummary(
              context,
              delegateSymbol,
              invokeMethod.Parameters[parameterIndex],
              parameterIndex,
              out var usageSummary) ||
            usageSummary.LambdaRewrites.Count == 0 ||
            usageSummary.MethodGroupTargets.Count > 0)
        {
            return false;
        }

        plan = new DelegateComplexShrinkPlan(
          delegateDeclaration,
          replacementDelegate,
          usageSummary);
        return true;
    }

    // 当委托只影响直接调用链时，生成声明与调用链同步收缩计划。
    public bool TryBuildDelegateInvocationChainPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out DelegateComplexShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveDelegateParameter(typeSyntax, out var delegateDeclaration, out _, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(delegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
            delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
            parameterIndex >= invokeMethod.Parameters.Length ||
            !TryBuildReplacementDelegate(
              delegateDeclaration,
              delegateDeclaration.ParameterList.Parameters[parameterIndex],
              out var replacementDelegate) ||
            !TryCollectDelegateUsageSummary(
              context,
              delegateSymbol,
              invokeMethod.Parameters[parameterIndex],
              parameterIndex,
              out var usageSummary) ||
            usageSummary.InvocationRewrites.Count == 0 ||
            usageSummary.MethodGroupTargets.Count > 0 ||
            usageSummary.LambdaRewrites.Count > 0)
        {
            return false;
        }

        plan = new DelegateComplexShrinkPlan(
          delegateDeclaration,
          replacementDelegate,
          usageSummary);
        return true;
    }

    // 仅在扩展方法接收者不变且非首参可安全删除时，生成方法与映射调用点收缩计划。
    public bool TryBuildExtensionReceiverNonFirstParameterPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, out MethodParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveMethodParameter(
              typeSyntax,
              MethodProposalSafety.IsSafeExtensionReceiverMethod,
              out var method,
              out var parameter,
              out var parameterIndex) ||
            parameterIndex <= 0 ||
            method.ParameterList.Parameters.FirstOrDefault() is not ParameterSyntax receiverParameter ||
            !receiverParameter.Modifiers.Any(token => token.IsKind(SyntaxKind.ThisKeyword)) ||
            context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            HasConflictingReplacementOverload(methodSymbol, parameterIndex) ||
            !TryBuildReplacementMethod(method, parameter, out var replacementMethod) ||
            !TryCollectMappedInvocationRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              methodSymbol,
              methodSymbol.Parameters[parameterIndex],
              requireCallsites: true,
              out var invocationRewrites))
        {
            return false;
        }

        plan = new MethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    private static bool TryBuildMethodPlan(ISemanticRuleContext context, TypeSyntax typeSyntax, Func<MethodDeclarationSyntax, bool> methodGuard, bool requireCallsites, out MethodParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveMethodParameter(typeSyntax, methodGuard, out var method, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementMethod(method, parameter, out var replacementMethod) ||
            !TryCollectInvocationRewrites(
              context.Runtime,
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterIndex,
              method.ParameterList.Parameters.Count,
              requireCallsites,
              out var invocationRewrites))
        {
            return false;
        }

        plan = new MethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    private static bool TryResolveMethodParameter(TypeSyntax typeSyntax, Func<MethodDeclarationSyntax, bool> methodGuard, out MethodDeclarationSyntax method, out ParameterSyntax parameter, out int parameterIndex)
    {
        method = null!;
        parameter = null!;
        parameterIndex = -1;

        parameter = typeSyntax.Ancestors()
          .OfType<ParameterSyntax>()
          .FirstOrDefault(candidate => candidate.Type?.Span.Contains(typeSyntax.Span) == true)!;
        method = parameter?.Parent?.Parent as MethodDeclarationSyntax ?? null!;
        if (parameter is null ||
            method is null ||
            !methodGuard(method))
        {
            return false;
        }

        parameterIndex = method.ParameterList.Parameters.IndexOf(parameter);
        return parameterIndex >= 0;
    }

    private static bool TryResolveSupportedMethodParameter(ISemanticRuleContext context, TypeSyntax typeSyntax, out MethodDeclarationSyntax method, out IMethodSymbol methodSymbol, out ParameterSyntax parameter, out IParameterSymbol parameterSymbol, out int parameterIndex)
    {
        method = null!;
        methodSymbol = null!;
        parameter = null!;
        parameterSymbol = null!;
        parameterIndex = -1;

        if (!TryResolveMethodParameter(
              typeSyntax,
              MethodProposalSafety.IsSafePrivateMethod,
              out method,
              out parameter,
              out parameterIndex) &&
            !TryResolveMethodParameter(
              typeSyntax,
              MethodProposalSafety.IsSafeNonPrivateMethod,
              out method,
              out parameter,
              out parameterIndex))
        {
            return false;
        }

        if (context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol resolvedMethodSymbol ||
            parameterIndex >= resolvedMethodSymbol.Parameters.Length)
        {
            return false;
        }

        methodSymbol = resolvedMethodSymbol;
        parameterSymbol = methodSymbol.Parameters[parameterIndex];
        return true;
    }

    // 把 TypeSyntax 解析回所属局部函数参数及其索引，供局部函数收缩链路复用。
    public static bool TryResolveLocalFunctionParameter(TypeSyntax typeSyntax, out LocalFunctionStatementSyntax localFunction, out ParameterSyntax parameter, out int parameterIndex)
    {
        localFunction = null!;
        parameter = null!;
        parameterIndex = -1;

        parameter = typeSyntax.Ancestors()
          .OfType<ParameterSyntax>()
          .FirstOrDefault(candidate => candidate.Type?.Span.Contains(typeSyntax.Span) == true)!;
        localFunction = parameter?.Parent?.Parent as LocalFunctionStatementSyntax ?? null!;
        if (parameter is null || localFunction is null)
        {
            return false;
        }

        parameterIndex = localFunction.ParameterList.Parameters.IndexOf(parameter);
        return parameterIndex >= 0;
    }

    // 把 TypeSyntax 解析回所属索引器参数及其索引，并排除接口索引器。
    public static bool TryResolveIndexerParameter(TypeSyntax typeSyntax, out IndexerDeclarationSyntax indexer, out ParameterSyntax parameter, out int parameterIndex)
    {
        indexer = null!;
        parameter = null!;
        parameterIndex = -1;

        parameter = typeSyntax.Ancestors()
          .OfType<ParameterSyntax>()
          .FirstOrDefault(candidate => candidate.Type?.Span.Contains(typeSyntax.Span) == true)!;
        indexer = parameter?.Parent?.Parent as IndexerDeclarationSyntax ?? null!;
        if (parameter is null ||
            indexer is null ||
            indexer.Parent is InterfaceDeclarationSyntax)
        {
            return false;
        }

        parameterIndex = indexer.ParameterList.Parameters.IndexOf(parameter);
        return parameterIndex >= 0;
    }

    // 把 TypeSyntax 解析回所属委托参数及其索引，供委托收缩链路复用。
    public static bool TryResolveDelegateParameter(TypeSyntax typeSyntax, out DelegateDeclarationSyntax delegateDeclaration, out ParameterSyntax parameter, out int parameterIndex)
    {
        delegateDeclaration = null!;
        parameter = null!;
        parameterIndex = -1;

        parameter = typeSyntax.Ancestors()
          .OfType<ParameterSyntax>()
          .FirstOrDefault(candidate => candidate.Type?.Span.Contains(typeSyntax.Span) == true)!;
        delegateDeclaration = parameter?.Parent?.Parent as DelegateDeclarationSyntax ?? null!;
        if (parameter is null || delegateDeclaration is null)
        {
            return false;
        }

        parameterIndex = delegateDeclaration.ParameterList.Parameters.IndexOf(parameter);
        return parameterIndex >= 0;
    }

    // 汇总委托参数删除需要同步改写的方法组、局部函数、lambda 与直接调用链事实。
    public static bool TryCollectDelegateUsageSummary(ISemanticRuleContext context, INamedTypeSymbol delegateSymbol, IParameterSymbol parameterSymbol, int parameterIndex, out DelegateUsageSummary usageSummary)
    {
        var methodRewrites = new ConcurrentBag<MethodRewrite>();
        var localFunctionRewrites = new ConcurrentBag<LocalFunctionRewrite>();
        var lambdaRewrites = new ConcurrentBag<ExpressionRewrite>();
        var invocationRewrites = new ConcurrentBag<InvocationRewrite>();
        var methodGroupTargets = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var handledInvocations = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var handledLambdaSpans = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var handledMethodRewrites = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var handledLocalFunctionRewrites = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var failed = 0;

        ForEachScan(
          GetTreeScans(context.SemanticModel.Compilation, context.Runtime),
          context.Runtime,
          (scan, stop) =>
          {
              foreach (var invocation in scan.GetInvocationBindings(delegateSymbol.DelegateInvokeMethod!))
              {
                  if (invocation.MethodSymbol is not IMethodSymbol targetMethod ||
                      targetMethod.MethodKind != MethodKind.DelegateInvoke ||
                      !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, delegateSymbol))
                  {
                      continue;
                  }

                  if (!handledInvocations.TryAdd(BuildSyntaxKey(invocation.Invocation), 0) ||
                      !TryBuildMappedInvocationReplacement(
                        invocation.Invocation,
                        invocation.Operation,
                        parameterSymbol,
                        out var replacementInvocation))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      stop();
                      return;
                  }

                  invocationRewrites.Add(
                    new InvocationRewrite(invocation.Invocation, replacementInvocation));
              }

              foreach (var expression in scan.GetExpressionBindings(delegateSymbol))
              {
                  if (!SymbolEqualityComparer.Default.Equals(expression.ConvertedType, delegateSymbol))
                  {
                      continue;
                  }

                  switch (expression.Operation)
                  {
                      case IMethodReferenceOperation methodReference:
                          if (!TryBuildMethodGroupTargetRewrite(
                                context,
                                methodReference.Method,
                                parameterIndex,
                                out var methodRewrite,
                                out var localFunctionRewrite))
                          {
                              Interlocked.Exchange(ref failed, 1);
                              stop();
                              return;
                          }

                          methodGroupTargets.TryAdd(methodReference.Method.ToDisplayString(), 0);
                          if (methodRewrite is not null &&
                              handledMethodRewrites.TryAdd(BuildSyntaxKey(methodRewrite.Method), 0))
                          {
                              methodRewrites.Add(methodRewrite);
                          }

                          if (localFunctionRewrite is not null &&
                              handledLocalFunctionRewrites.TryAdd(
                                BuildSyntaxKey(localFunctionRewrite.LocalFunction),
                                0))
                          {
                              localFunctionRewrites.Add(localFunctionRewrite);
                          }
                          break;

                      case IAnonymousFunctionOperation anonymousFunction:
                          var key = $"{scan.SyntaxTree.FilePath}:{expression.Expression.SpanStart}:{expression.Expression.Span.Length}";
                          if (!handledLambdaSpans.TryAdd(key, 0) ||
                              !TryBuildLambdaRewrite(
                                context,
                                scan.SemanticModel,
                                expression.Expression,
                                anonymousFunction,
                                parameterIndex,
                                out var lambdaRewrite))
                          {
                              Interlocked.Exchange(ref failed, 1);
                              stop();
                              return;
                          }

                          lambdaRewrites.Add(lambdaRewrite);
                          break;
                  }
              }
          });

        if (Volatile.Read(ref failed) != 0)
        {
            usageSummary = null!;
            return false;
        }

        usageSummary = new DelegateUsageSummary(
          methodRewrites.OrderBy(static item => BuildSyntaxKey(item.Method), StringComparer.Ordinal).ToList(),
          localFunctionRewrites.OrderBy(static item => BuildSyntaxKey(item.LocalFunction), StringComparer.Ordinal).ToList(),
          lambdaRewrites.OrderBy(static item => BuildSyntaxKey(item.Expression), StringComparer.Ordinal).ToList(),
          invocationRewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList(),
          methodGroupTargets.Keys.ToHashSet(StringComparer.Ordinal));
        return true;
    }

    private static bool HasUnsupportedParameterShape(ParameterSyntax parameter, IParameterSymbol parameterSymbol)
    {
        return parameter.Default is not null ||
          parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.ParamsKeyword)) ||
          parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.ThisKeyword)) ||
          parameterSymbol.IsOptional ||
          parameterSymbol.IsParams;
    }

    private static bool HasUnsupportedOptionalParameterShape(ParameterSyntax parameter, IParameterSymbol parameterSymbol)
    {
        return parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.ParamsKeyword)) ||
          parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.ThisKeyword)) ||
          parameterSymbol.IsParams;
    }

    private static bool IsOptionalParameter(ParameterSyntax parameter, IParameterSymbol parameterSymbol)
    {
        return parameter.Default is not null || parameterSymbol.IsOptional;
    }

    private static bool IsParamsParameter(ParameterSyntax parameter, IParameterSymbol parameterSymbol)
    {
        return parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.ParamsKeyword)) || parameterSymbol.IsParams;
    }

    // 从方法声明中删除目标参数，并验证参数列表长度确实减少一位。
    public static bool TryBuildReplacementMethod(MethodDeclarationSyntax method, ParameterSyntax parameter, out MethodDeclarationSyntax replacementMethod)
    {
        var replacementParameters = method.ParameterList.Parameters.Remove(parameter);
        replacementMethod = method.WithParameterList(method.ParameterList.WithParameters(replacementParameters));
        return replacementParameters.Count + 1 == method.ParameterList.Parameters.Count;
    }

    // 从局部函数声明中删除目标参数，并验证参数列表长度确实减少一位。
    public static bool TryBuildReplacementLocalFunction(LocalFunctionStatementSyntax localFunction, ParameterSyntax parameter, out LocalFunctionStatementSyntax replacementLocalFunction)
    {
        var replacementParameters = localFunction.ParameterList.Parameters.Remove(parameter);
        replacementLocalFunction = localFunction.WithParameterList(
          localFunction.ParameterList.WithParameters(replacementParameters));
        return replacementParameters.Count + 1 == localFunction.ParameterList.Parameters.Count;
    }

    // 从索引器声明中删除目标参数，并验证参数列表长度确实减少一位。
    public static bool TryBuildReplacementIndexer(IndexerDeclarationSyntax indexer, ParameterSyntax parameter, out IndexerDeclarationSyntax replacementIndexer)
    {
        var replacementParameters = indexer.ParameterList.Parameters.Remove(parameter);
        replacementIndexer = indexer.WithParameterList(indexer.ParameterList.WithParameters(replacementParameters));
        return replacementParameters.Count + 1 == indexer.ParameterList.Parameters.Count;
    }

    // 从委托声明中删除目标参数，并验证参数列表长度确实减少一位。
    public static bool TryBuildReplacementDelegate(DelegateDeclarationSyntax delegateDeclaration, ParameterSyntax parameter, out DelegateDeclarationSyntax replacementDelegate)
    {
        var replacementParameters = delegateDeclaration.ParameterList.Parameters.Remove(parameter);
        replacementDelegate = delegateDeclaration.WithParameterList(
          delegateDeclaration.ParameterList.WithParameters(replacementParameters));
        return replacementParameters.Count + 1 == delegateDeclaration.ParameterList.Parameters.Count;
    }

    private static bool TryCollectInvocationRewrites( AnalysisRuntime runtime, Compilation compilation, IMethodSymbol methodSymbol, int parameterIndex, int expectedParameterCount, bool requireCallsites, out List<InvocationRewrite> invocationRewrites)
    {
        var rewrites = new ConcurrentBag<InvocationRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        ForEachScan(
          GetTreeScans(compilation, runtime),
          runtime,
          (scan, stop) =>
          {
              foreach (var invocation in scan.GetInvocationBindings(methodSymbol))
              {
                  if (invocation.MethodSymbol is not IMethodSymbol targetMethod ||
                      !SymbolEqualityComparer.Default.Equals(methodSymbol, targetMethod))
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildReplacementInvocation(
                        invocation.Invocation,
                        parameterIndex,
                        expectedParameterCount,
                        out var replacementInvocation))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      stop();
                      return;
                  }

                  rewrites.Add(
                    new InvocationRewrite(invocation.Invocation, replacementInvocation));
              }
          });

        invocationRewrites = Volatile.Read(ref failed) != 0
          ? new List<InvocationRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryCollectNamedArgumentInvocationRewrites( AnalysisRuntime runtime, Compilation compilation, IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol, out List<InvocationRewrite> invocationRewrites)
    {
        var rewrites = new ConcurrentBag<InvocationRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        ForEachScan(
          GetTreeScans(compilation, runtime),
          runtime,
          (scan, stop) =>
          {
              foreach (var invocation in scan.GetInvocationBindings(methodSymbol))
              {
                  if (invocation.MethodSymbol is not IMethodSymbol targetMethod ||
                      !SymbolEqualityComparer.Default.Equals(methodSymbol, targetMethod) ||
                      invocation.Operation is not IInvocationOperation invocationOperation)
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildNamedArgumentReplacementInvocation(
                        invocation.Invocation,
                        invocationOperation,
                        parameterSymbol,
                        out var replacementInvocation))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      stop();
                      return;
                  }

                  rewrites.Add(
                    new InvocationRewrite(invocation.Invocation, replacementInvocation));
              }
          });

        invocationRewrites = Volatile.Read(ref failed) != 0
          ? new List<InvocationRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          Volatile.Read(ref matchedCallsites) > 0;
    }

    private static bool TryCollectOptionalInvocationRewrites( AnalysisRuntime runtime, Compilation compilation, IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol, bool requireCallsites, out List<InvocationRewrite> invocationRewrites)
    {
        var matchedCallsites = 0;
        var rewrites = new ConcurrentBag<InvocationRewrite>();
        var failed = 0;

        ForEachScan(
          GetTreeScans(compilation, runtime),
          runtime,
          (scan, stop) =>
          {
              foreach (var invocation in scan.GetInvocationBindings(methodSymbol))
              {
                  if (invocation.MethodSymbol is not IMethodSymbol targetMethod ||
                      !SymbolEqualityComparer.Default.Equals(methodSymbol, targetMethod) ||
                      invocation.Operation is not IInvocationOperation invocationOperation)
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildOptionalReplacementInvocation(
                        invocation.Invocation,
                        invocationOperation,
                        parameterSymbol,
                        out var replacementInvocation,
                        out var changed))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      stop();
                      return;
                  }

                  if (changed)
                  {
                      rewrites.Add(
                        new InvocationRewrite(invocation.Invocation, replacementInvocation));
                  }
              }
          });

        invocationRewrites = Volatile.Read(ref failed) != 0
          ? new List<InvocationRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryCollectParamsInvocationRewrites( AnalysisRuntime runtime, Compilation compilation, IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol, bool requireCallsites, out List<InvocationRewrite> invocationRewrites)
    {
        invocationRewrites = new List<InvocationRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        ForEachScan(
          GetTreeScans(compilation, runtime),
          runtime,
          (scan, stop) =>
          {
              foreach (var invocation in scan.GetInvocationBindings(methodSymbol))
              {
                  if (invocation.MethodSymbol is not IMethodSymbol targetMethod ||
                      !SymbolEqualityComparer.Default.Equals(methodSymbol, targetMethod) ||
                      invocation.Operation is not IInvocationOperation invocationOperation)
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  var paramsArguments = invocationOperation.Arguments
                    .Where(argument => SymbolEqualityComparer.Default.Equals(argument.Parameter, parameterSymbol))
                    .ToList();
                  if (paramsArguments.Any(argument => !argument.IsImplicit))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      stop();
                      return;
                  }
              }
          });

        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryCollectElementAccessRewrites( AnalysisRuntime runtime, Compilation compilation, IPropertySymbol indexerSymbol, int parameterIndex, int expectedParameterCount, bool requireCallsites, out List<ElementAccessRewrite> accessRewrites)
    {
        var rewrites = new ConcurrentBag<ElementAccessRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        ForEachScan(
          GetTreeScans(compilation, runtime),
          runtime,
          (scan, stop) =>
          {
              foreach (var elementAccess in scan.GetElementAccessBindings(indexerSymbol))
              {
                  if (elementAccess.PropertySymbol is not IPropertySymbol targetIndexer ||
                      !SymbolEqualityComparer.Default.Equals(indexerSymbol, targetIndexer))
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildReplacementElementAccess(
                        elementAccess.ElementAccess,
                        parameterIndex,
                        expectedParameterCount,
                        out var replacementElementAccess))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      stop();
                      return;
                  }

                  rewrites.Add(
                    new ElementAccessRewrite(elementAccess.ElementAccess, replacementElementAccess));
              }
          });

        accessRewrites = Volatile.Read(ref failed) != 0
          ? new List<ElementAccessRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.ElementAccess), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryCollectNamedElementAccessRewrites( AnalysisRuntime runtime, Compilation compilation, IPropertySymbol indexerSymbol, IParameterSymbol parameterSymbol, out List<ElementAccessRewrite> accessRewrites)
    {
        var rewrites = new ConcurrentBag<ElementAccessRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        ForEachScan(
          GetTreeScans(compilation, runtime),
          runtime,
          (scan, stop) =>
          {
              foreach (var elementAccess in scan.GetElementAccessBindings(indexerSymbol))
              {
                  if (elementAccess.PropertySymbol is not IPropertySymbol targetIndexer ||
                      !SymbolEqualityComparer.Default.Equals(indexerSymbol, targetIndexer) ||
                      elementAccess.Operation is not IPropertyReferenceOperation propertyReference)
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildNamedArgumentReplacementElementAccess(
                        elementAccess.ElementAccess,
                        propertyReference,
                        parameterSymbol,
                        out var replacementElementAccess))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      stop();
                      return;
                  }

                  rewrites.Add(
                    new ElementAccessRewrite(elementAccess.ElementAccess, replacementElementAccess));
              }
          });

        accessRewrites = Volatile.Read(ref failed) != 0
          ? new List<ElementAccessRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.ElementAccess), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          Volatile.Read(ref matchedCallsites) > 0;
    }

    private static bool TryCollectMappedInvocationRewrites( AnalysisRuntime runtime, Compilation compilation, IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol, bool requireCallsites, out List<InvocationRewrite> invocationRewrites)
    {
        var rewrites = new ConcurrentBag<InvocationRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        ForEachScan(
          GetTreeScans(compilation, runtime),
          runtime,
          (scan, stop) =>
          {
              foreach (var invocation in scan.GetMappedInvocationBindings(methodSymbol))
              {
                  if (invocation.MethodSymbol is not IMethodSymbol targetMethod ||
                      !MethodMatchesInvocationTarget(methodSymbol, targetMethod))
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildMappedInvocationReplacement(
                        invocation.Invocation,
                        invocation.Operation,
                        parameterSymbol,
                        out var replacementInvocation))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      stop();
                      return;
                  }

                  rewrites.Add(
                    new InvocationRewrite(invocation.Invocation, replacementInvocation));
              }
          });

        invocationRewrites = Volatile.Read(ref failed) != 0
          ? new List<InvocationRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryResolveMethodSymbol(SemanticModel semanticModel, InvocationExpressionSyntax invocation, out IMethodSymbol methodSymbol)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, CancellationToken.None);
        methodSymbol = symbolInfo.Symbol as IMethodSymbol
          ?? symbolInfo.CandidateSymbols
            .OfType<IMethodSymbol>()
            .SingleOrDefault()!;
        return methodSymbol is not null;
    }

    private static bool TryResolveIndexerSymbol(SemanticModel semanticModel, ElementAccessExpressionSyntax elementAccess, out IPropertySymbol indexerSymbol)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(elementAccess, CancellationToken.None);
        indexerSymbol = symbolInfo.Symbol as IPropertySymbol
          ?? symbolInfo.CandidateSymbols
            .OfType<IPropertySymbol>()
            .SingleOrDefault()!;
        return indexerSymbol is not null;
    }

    private static bool MethodMatchesInvocationTarget(IMethodSymbol declaredMethod, IMethodSymbol targetMethod)
    {
        return SymbolEqualityComparer.Default.Equals(declaredMethod, targetMethod) ||
          SymbolEqualityComparer.Default.Equals(declaredMethod, targetMethod.ReducedFrom) ||
          SymbolEqualityComparer.Default.Equals(declaredMethod.OriginalDefinition, targetMethod.OriginalDefinition) ||
          (targetMethod.ReducedFrom is not null &&
           SymbolEqualityComparer.Default.Equals(declaredMethod.OriginalDefinition, targetMethod.ReducedFrom.OriginalDefinition));
    }

    // 删除位置参数调用中的目标实参，并要求实参数量与声明参数数量完全对齐。
    public static bool TryBuildReplacementInvocation(InvocationExpressionSyntax invocation, int parameterIndex, int expectedParameterCount, out InvocationExpressionSyntax replacementInvocation)
    {
        replacementInvocation = null!;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != expectedParameterCount ||
            parameterIndex >= arguments.Count ||
            arguments.Any(argument => argument.NameColon is not null))
        {
            return false;
        }

        replacementInvocation = invocation.WithArgumentList(
          invocation.ArgumentList.WithArguments(arguments.RemoveAt(parameterIndex)));
        return true;
    }

    // 删除命名参数调用中的目标实参，并保持其他命名参数顺序不变。
    public static bool TryBuildNamedArgumentReplacementInvocation(InvocationExpressionSyntax invocation, IInvocationOperation invocationOperation, IParameterSymbol parameterSymbol, out InvocationExpressionSyntax replacementInvocation)
    {
        replacementInvocation = null!;

        var argumentsToRemove = invocationOperation.Arguments
          .Where(argument =>
            SymbolEqualityComparer.Default.Equals(argument.Parameter, parameterSymbol))
          .Select(argument => argument.Syntax)
          .OfType<ArgumentSyntax>()
          .ToList();
        if (argumentsToRemove.Count != 1 || argumentsToRemove[0].NameColon is null)
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        var targetArgument = argumentsToRemove[0];
        var argumentIndex = arguments.IndexOf(targetArgument);
        if (argumentIndex < 0)
        {
            return false;
        }

        replacementInvocation = invocation.WithArgumentList(
          invocation.ArgumentList.WithArguments(arguments.RemoveAt(argumentIndex)));
        return true;
    }

    // 在可选参数调用中删除显式传入的目标实参；若本来就省略则保持原调用不变。
    public static bool TryBuildOptionalReplacementInvocation(InvocationExpressionSyntax invocation, IInvocationOperation invocationOperation, IParameterSymbol parameterSymbol, out InvocationExpressionSyntax replacementInvocation, out bool changed)
    {
        replacementInvocation = invocation;
        changed = false;

        var argumentsToRemove = invocationOperation.Arguments
          .Where(argument =>
            SymbolEqualityComparer.Default.Equals(argument.Parameter, parameterSymbol))
          .Select(argument => argument.Syntax)
          .OfType<ArgumentSyntax>()
          .ToList();
        if (argumentsToRemove.Count > 1)
        {
            return false;
        }

        if (argumentsToRemove.Count == 0)
        {
            return true;
        }

        var arguments = invocation.ArgumentList.Arguments;
        var targetArgument = argumentsToRemove[0];
        var argumentIndex = arguments.IndexOf(targetArgument);
        if (argumentIndex < 0)
        {
            return false;
        }

        replacementInvocation = invocation.WithArgumentList(
          invocation.ArgumentList.WithArguments(arguments.RemoveAt(argumentIndex)));
        changed = true;
        return true;
    }

    // 删除映射到扩展方法目标参数的调用实参，供扩展方法收缩链路复用。
    public static bool TryBuildMappedInvocationReplacement(InvocationExpressionSyntax invocation, IInvocationOperation? invocationOperation, IParameterSymbol parameterSymbol, out InvocationExpressionSyntax replacementInvocation)
    {
        replacementInvocation = null!;
        if (invocationOperation is null)
        {
            return false;
        }

        var argumentsToRemove = invocationOperation.Arguments
          .Where(argument => SymbolEqualityComparer.Default.Equals(argument.Parameter, parameterSymbol))
          .Select(argument => argument.Syntax)
          .OfType<ArgumentSyntax>()
          .ToList();
        if (argumentsToRemove.Count != 1)
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        var targetArgument = argumentsToRemove[0];
        var argumentIndex = arguments.IndexOf(targetArgument);
        if (argumentIndex < 0)
        {
            return false;
        }

        replacementInvocation = invocation.WithArgumentList(
          invocation.ArgumentList.WithArguments(arguments.RemoveAt(argumentIndex)));
        return true;
    }

    // 删除命名索引实参中的目标槽位，并保持其余命名实参顺序不变。
    public static bool TryBuildNamedArgumentReplacementElementAccess(ElementAccessExpressionSyntax elementAccess, IPropertyReferenceOperation propertyReference, IParameterSymbol parameterSymbol, out ElementAccessExpressionSyntax replacementElementAccess)
    {
        replacementElementAccess = null!;

        var argumentsToRemove = propertyReference.Arguments
          .Where(argument => SymbolEqualityComparer.Default.Equals(argument.Parameter, parameterSymbol))
          .Select(argument => argument.Syntax)
          .OfType<ArgumentSyntax>()
          .ToList();
        if (argumentsToRemove.Count != 1 || argumentsToRemove[0].NameColon is null)
        {
            return false;
        }

        var arguments = elementAccess.ArgumentList.Arguments;
        var targetArgument = argumentsToRemove[0];
        var argumentIndex = arguments.IndexOf(targetArgument);
        if (argumentIndex < 0)
        {
            return false;
        }

        replacementElementAccess = elementAccess.WithArgumentList(
          elementAccess.ArgumentList.WithArguments(arguments.RemoveAt(argumentIndex)));
        return true;
    }

    private static bool TryBuildMethodGroupTargetRewrite(ISemanticRuleContext context, IMethodSymbol targetMethod, int parameterIndex, out MethodRewrite? methodRewrite, out LocalFunctionRewrite? localFunctionRewrite)
    {
        methodRewrite = null;
        localFunctionRewrite = null;

        if (targetMethod.IsExtensionMethod ||
            parameterIndex >= targetMethod.Parameters.Length)
        {
            return false;
        }

        var parameterSymbol = targetMethod.Parameters[parameterIndex];
        var syntaxReference = targetMethod.DeclaringSyntaxReferences.SingleOrDefault();
        if (syntaxReference?.GetSyntax(CancellationToken.None) is MethodDeclarationSyntax methodDeclaration)
        {
            if (!MethodProposalSafety.IsSafePrivateMethod(methodDeclaration) &&
                !MethodProposalSafety.IsSafeNonPrivateMethod(methodDeclaration) &&
                !MethodProposalSafety.IsSafeExtensionReceiverMethod(methodDeclaration))
            {
                return false;
            }

            var parameter = methodDeclaration.ParameterList.Parameters.ElementAtOrDefault(parameterIndex);
            if (parameter is null ||
                HasUnsupportedParameterShape(parameter, parameterSymbol) ||
                HasConflictingReplacementOverload(targetMethod, parameterIndex) ||
                !TryBuildReplacementMethod(methodDeclaration, parameter, out var replacementMethod))
            {
                return false;
            }

            methodRewrite = new MethodRewrite(methodDeclaration, replacementMethod);
            return true;
        }

        if (syntaxReference?.GetSyntax(CancellationToken.None) is LocalFunctionStatementSyntax localFunction)
        {
            var parameter = localFunction.ParameterList.Parameters.ElementAtOrDefault(parameterIndex);
            if (parameter is null ||
                HasUnsupportedParameterShape(parameter, parameterSymbol) ||
                !TryBuildReplacementLocalFunction(localFunction, parameter, out var replacementLocalFunction))
            {
                return false;
            }

            localFunctionRewrite = new LocalFunctionRewrite(localFunction, replacementLocalFunction);
            return true;
        }

        return false;
    }

    // 为 lambda 绑定生成删除目标参数后的替换表达式，同时保持委托签名兼容。
    public static bool TryBuildLambdaRewrite(ISemanticRuleContext context, SemanticModel semanticModel, ExpressionSyntax expression, IAnonymousFunctionOperation anonymousFunction, int parameterIndex, out ExpressionRewrite lambdaRewrite)
    {
        lambdaRewrite = null!;
        if (parameterIndex >= anonymousFunction.Symbol.Parameters.Length)
        {
            return false;
        }

        switch (expression)
        {
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                return TryBuildParenthesizedLambdaRewrite(
                  semanticModel,
                  parenthesizedLambda,
                  anonymousFunction.Symbol.Parameters[parameterIndex],
                  parameterIndex,
                  out lambdaRewrite);

            case SimpleLambdaExpressionSyntax simpleLambda:
                return TryBuildSimpleLambdaRewrite(
                  semanticModel,
                  simpleLambda,
                  anonymousFunction.Symbol.Parameters[parameterIndex],
                  parameterIndex,
                  out lambdaRewrite);

            case AnonymousMethodExpressionSyntax anonymousMethod:
                return TryBuildAnonymousMethodRewrite(
                  semanticModel,
                  anonymousMethod,
                  anonymousFunction.Symbol.Parameters[parameterIndex],
                  parameterIndex,
                  out lambdaRewrite);

            default:
                _ = context;
                return false;
        }
    }

    private static bool TryBuildParenthesizedLambdaRewrite(SemanticModel semanticModel, ParenthesizedLambdaExpressionSyntax lambda, IParameterSymbol parameterSymbol, int parameterIndex, out ExpressionRewrite rewrite)
    {
        rewrite = null!;
        var parameter = lambda.ParameterList.Parameters.ElementAtOrDefault(parameterIndex);
        if (parameter is null ||
            IsLambdaParameterUsed(semanticModel, lambda, parameterSymbol) ||
            parameter.Modifiers.Count > 0)
        {
            return false;
        }

        var replacementParameters = lambda.ParameterList.Parameters.RemoveAt(parameterIndex);
        var replacement = lambda.WithParameterList(
          lambda.ParameterList.WithParameters(replacementParameters));
        rewrite = new ExpressionRewrite(lambda, replacement);
        return true;
    }

    private static bool TryBuildSimpleLambdaRewrite(SemanticModel semanticModel, SimpleLambdaExpressionSyntax lambda, IParameterSymbol parameterSymbol, int parameterIndex, out ExpressionRewrite rewrite)
    {
        rewrite = null!;
        if (parameterIndex != 0 ||
            IsLambdaParameterUsed(semanticModel, lambda, parameterSymbol) ||
            lambda.Parameter.Modifiers.Count > 0)
        {
            return false;
        }

        var bodyText = lambda.Block is not null
          ? lambda.Block.WithoutTrivia().ToFullString()
          : lambda.ExpressionBody!.WithoutTrivia().ToFullString();
        var asyncPrefix = lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)
          ? "async "
          : string.Empty;
        var replacement = (ExpressionSyntax)SyntaxFactory.ParseExpression(
          $"{asyncPrefix}() => {bodyText}");
        rewrite = new ExpressionRewrite(lambda, replacement);
        return true;
    }

    private static bool TryBuildAnonymousMethodRewrite(SemanticModel semanticModel, AnonymousMethodExpressionSyntax anonymousMethod, IParameterSymbol parameterSymbol, int parameterIndex, out ExpressionRewrite rewrite)
    {
        rewrite = null!;
        if (anonymousMethod.ParameterList is null)
        {
            return false;
        }

        var parameter = anonymousMethod.ParameterList.Parameters.ElementAtOrDefault(parameterIndex);
        if (parameter is null ||
            IsLambdaParameterUsed(semanticModel, anonymousMethod, parameterSymbol) ||
            parameter.Modifiers.Count > 0)
        {
            return false;
        }

        var replacementParameters = anonymousMethod.ParameterList.Parameters.RemoveAt(parameterIndex);
        var replacement = anonymousMethod.WithParameterList(
          anonymousMethod.ParameterList.WithParameters(replacementParameters));
        rewrite = new ExpressionRewrite(anonymousMethod, replacement);
        return true;
    }

    private static bool IsLambdaParameterUsed(SemanticModel semanticModel, SyntaxNode lambdaRoot, IParameterSymbol parameterSymbol)
    {
        return lambdaRoot.DescendantNodes()
          .OfType<IdentifierNameSyntax>()
          .Any(identifier =>
          {
              var symbol = semanticModel.GetSymbolInfo(identifier, CancellationToken.None).Symbol;
              return SymbolEqualityComparer.Default.Equals(symbol, parameterSymbol);
          });
    }

    private static string BuildSyntaxKey(SyntaxNode node)
    {
        return $"{node.SyntaxTree.FilePath}:{node.SpanStart}:{node.Span.Length}:{node.RawKind}";
    }

    // 删除位置索引访问中的目标实参，并要求实参数量与声明参数数量完全对齐。
    public static bool TryBuildReplacementElementAccess(ElementAccessExpressionSyntax elementAccess, int parameterIndex, int expectedParameterCount, out ElementAccessExpressionSyntax replacementElementAccess)
    {
        replacementElementAccess = null!;

        var arguments = elementAccess.ArgumentList.Arguments;
        if (arguments.Count != expectedParameterCount ||
            parameterIndex >= arguments.Count ||
            arguments.Any(argument => argument.NameColon is not null))
        {
            return false;
        }

        replacementElementAccess = elementAccess.WithArgumentList(
          elementAccess.ArgumentList.WithArguments(arguments.RemoveAt(parameterIndex)));
        return true;
    }

    // 判断委托类型是否仍有无法一并改写的剩余引用，用来阻止不完整的签名收缩。
    public static bool HasDelegateReferences( AnalysisRuntime runtime, Compilation compilation, INamedTypeSymbol delegateSymbol)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var scan = GetTreeScan(compilation, tree, runtime);
            foreach (var typeSyntax in scan.GetTypeSyntaxBindings(delegateSymbol))
            {
                if (SymbolEqualityComparer.Default.Equals(typeSyntax.Symbol, delegateSymbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<TreeScan> GetTreeScans(Compilation compilation,  AnalysisRuntime runtime)
    {
        var cache = runtime.GetOrCreateCompilationCache(
          compilation,
          static key => new CompilationScanCache(key));
        return compilation.SyntaxTrees
          .Select(cache.GetTreeScan)
          .ToList();
    }

    private static TreeScan GetTreeScan(Compilation compilation, SyntaxTree tree,  AnalysisRuntime runtime)
    {
        return runtime.GetOrCreateCompilationCache(
          compilation,
          static key => new CompilationScanCache(key)).GetTreeScan(tree);
    }

    private static void ForEachScan(IReadOnlyList<TreeScan> scans,  AnalysisRuntime runtime, Action<TreeScan, Action> visit)
    {
        var shouldStop = 0;
        void Stop()
        {
            Interlocked.Exchange(ref shouldStop, 1);
        }

        if (!runtime.ExecutionOptions.EnableHelperParallelism ||
            runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism == 1 ||
            scans.Count <= 1)
        {
            foreach (var scan in scans)
            {
                if (Volatile.Read(ref shouldStop) != 0)
                {
                    break;
                }

                visit(scan, Stop);
            }

            return;
        }

        runtime.ConcurrencyPool.ForEachAsync(
          scans,
          runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
          (scan, _, cancellationToken) =>
          {
              cancellationToken.ThrowIfCancellationRequested();
              if (Volatile.Read(ref shouldStop) == 0)
              {
                  visit(scan, Stop);
              }

              return Task.CompletedTask;
          },
          runtime.ExecutionOptions.CancellationToken).GetAwaiter().GetResult();
    }

    // 判断删除指定参数后，是否会与同名现有重载发生签名冲突。
    public static bool HasConflictingReplacementOverload(IMethodSymbol methodSymbol, int parameterIndex)
    {
        var replacementParameters = methodSymbol.Parameters
          .Where((_, index) => index != parameterIndex)
          .ToArray();

        foreach (var candidate in methodSymbol.ContainingType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>())
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, methodSymbol) ||
                candidate.MethodKind != methodSymbol.MethodKind ||
                candidate.Arity != methodSymbol.Arity ||
                candidate.IsExtensionMethod != methodSymbol.IsExtensionMethod ||
                candidate.Parameters.Length != replacementParameters.Length)
            {
                continue;
            }

            var matches = true;
            for (var index = 0; index < replacementParameters.Length; index++)
            {
                if (!SymbolEqualityComparer.Default.Equals(candidate.Parameters[index].Type, replacementParameters[index].Type) ||
                    candidate.Parameters[index].RefKind != replacementParameters[index].RefKind)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class CompilationScanCache
    {
        private readonly Compilation _compilation;
        private readonly ConcurrentDictionary<SyntaxTree, Lazy<TreeScan>> _treeScans;
        private int _materializedTreeCount;

        // 绑定到一次 Compilation，并按 SyntaxTree 延迟缓存扫描结果。
        public CompilationScanCache(Compilation compilation)
        {
            _compilation = compilation;
            _treeScans = new ConcurrentDictionary<SyntaxTree, Lazy<TreeScan>>(ReferenceEqualityComparer.Instance);
        }

        // 返回指定语法树的延迟扫描结果；首次命中时才真正物化索引。
        public TreeScan GetTreeScan(SyntaxTree tree)
        {
            return _treeScans.GetOrAdd(
              tree,
              currentTree => new Lazy<TreeScan>(
                () =>
                {
                    Interlocked.Increment(ref _materializedTreeCount);
                    return new TreeScan(_compilation, currentTree);
                },
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
    }

    private static IMethodSymbol? ResolveMethodSymbol(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        return TryResolveMethodSymbol(semanticModel, invocation, out var methodSymbol)
          ? methodSymbol
          : null;
    }

    private static IPropertySymbol? ResolveIndexerSymbol(SemanticModel semanticModel, ElementAccessExpressionSyntax elementAccess)
    {
        return TryResolveIndexerSymbol(semanticModel, elementAccess, out var indexerSymbol)
          ? indexerSymbol
          : null;
    }

    private static IEnumerable<IMethodSymbol> GetMethodLookupSymbols(IMethodSymbol methodSymbol)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        if (seen.Add(methodSymbol))
        {
            yield return methodSymbol;
        }

        if (methodSymbol.ReducedFrom is not null &&
            seen.Add(methodSymbol.ReducedFrom))
        {
            yield return methodSymbol.ReducedFrom;
        }

        if (seen.Add(methodSymbol.OriginalDefinition))
        {
            yield return methodSymbol.OriginalDefinition;
        }

        if (methodSymbol.ReducedFrom?.OriginalDefinition is IMethodSymbol reducedOriginal &&
            seen.Add(reducedOriginal))
        {
            yield return reducedOriginal;
        }
    }

    private static IEnumerable<IPropertySymbol> GetPropertyLookupSymbols(IPropertySymbol propertySymbol)
    {
        var seen = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        if (seen.Add(propertySymbol))
        {
            yield return propertySymbol;
        }

        if (seen.Add(propertySymbol.OriginalDefinition))
        {
            yield return propertySymbol.OriginalDefinition;
        }
    }

    private sealed class TreeScan
    {
        private readonly Compilation _compilation;
        private readonly Lazy<SemanticModel> _semanticModel;
        private readonly Lazy<IReadOnlyList<InvocationBinding>> _invocationBindings;
        private readonly Lazy<IReadOnlyList<ElementAccessBinding>> _elementAccessBindings;
        private readonly Lazy<IReadOnlyList<ExpressionBinding>> _expressionBindings;
        private readonly Lazy<IReadOnlyList<TypeSyntaxBinding>> _typeSyntaxBindings;
        private readonly Lazy<Dictionary<IMethodSymbol, IReadOnlyList<InvocationBinding>>> _invocationsByMethodSymbol;
        private readonly Lazy<Dictionary<IPropertySymbol, IReadOnlyList<ElementAccessBinding>>> _elementAccessesByPropertySymbol;
        private readonly Lazy<Dictionary<INamedTypeSymbol, IReadOnlyList<ExpressionBinding>>> _expressionsByConvertedType;
        private readonly Lazy<Dictionary<INamedTypeSymbol, IReadOnlyList<TypeSyntaxBinding>>> _typeSyntaxesByResolvedSymbol;
        private int _invocationIndexBuildCount;
        private int _elementAccessIndexBuildCount;
        private int _expressionIndexBuildCount;
        private int _typeSyntaxIndexBuildCount;

        // 绑定单棵语法树并初始化所有延迟索引，供参数收缩扫描重复复用。
        public TreeScan(Compilation compilation, SyntaxTree syntaxTree)
        {
            _compilation = compilation;
            SyntaxTree = syntaxTree;
            _semanticModel = CreateLazy(() => _compilation.GetSemanticModel(SyntaxTree));
            _invocationBindings = CreateLazy(BuildInvocationBindings);
            _elementAccessBindings = CreateLazy(BuildElementAccessBindings);
            _expressionBindings = CreateLazy(BuildExpressionBindings);
            _typeSyntaxBindings = CreateLazy(BuildTypeSyntaxBindings);
            _invocationsByMethodSymbol = CreateLazy(() =>
            {
                Interlocked.Increment(ref _invocationIndexBuildCount);
                return BuildInvocationIndex(_invocationBindings.Value);
            });
            _elementAccessesByPropertySymbol = CreateLazy(() =>
            {
                Interlocked.Increment(ref _elementAccessIndexBuildCount);
                return BuildElementAccessIndex(_elementAccessBindings.Value);
            });
            _expressionsByConvertedType = CreateLazy(() =>
            {
                Interlocked.Increment(ref _expressionIndexBuildCount);
                return BuildExpressionIndex(_expressionBindings.Value);
            });
            _typeSyntaxesByResolvedSymbol = CreateLazy(() =>
            {
                Interlocked.Increment(ref _typeSyntaxIndexBuildCount);
                return BuildTypeSyntaxIndex(_typeSyntaxBindings.Value);
            });
        }

        public SyntaxTree SyntaxTree { get; }

        public SemanticModel SemanticModel => _semanticModel.Value;

        // 返回解析到指定方法符号的直接调用绑定列表。
        public IReadOnlyList<InvocationBinding> GetInvocationBindings(IMethodSymbol methodSymbol)
        {
            return _invocationsByMethodSymbol.Value.TryGetValue(methodSymbol, out var bindings)
              ? bindings
              : Array.Empty<InvocationBinding>();
        }

        // 返回解析到指定方法符号的映射扩展调用绑定，并按语法位置去重。
        public IReadOnlyList<InvocationBinding> GetMappedInvocationBindings(IMethodSymbol methodSymbol)
        {
            var results = new Dictionary<string, InvocationBinding>(StringComparer.Ordinal);
            foreach (var lookupSymbol in GetMethodLookupSymbols(methodSymbol))
            {
                if (!_invocationsByMethodSymbol.Value.TryGetValue(lookupSymbol, out var bindings))
                {
                    continue;
                }

                foreach (var binding in bindings)
                {
                    results.TryAdd(BuildSyntaxKey(binding.Invocation), binding);
                }
            }

            return results.Values.ToList();
        }

        // 返回解析到指定索引器属性的元素访问绑定，并按语法位置去重。
        public IReadOnlyList<ElementAccessBinding> GetElementAccessBindings(IPropertySymbol propertySymbol)
        {
            var results = new Dictionary<string, ElementAccessBinding>(StringComparer.Ordinal);
            foreach (var lookupSymbol in GetPropertyLookupSymbols(propertySymbol))
            {
                if (!_elementAccessesByPropertySymbol.Value.TryGetValue(lookupSymbol, out var bindings))
                {
                    continue;
                }

                foreach (var binding in bindings)
                {
                    results.TryAdd(BuildSyntaxKey(binding.ElementAccess), binding);
                }
            }

            return results.Values.ToList();
        }

        // 返回转换到指定委托类型的表达式绑定列表。
        public IReadOnlyList<ExpressionBinding> GetExpressionBindings(INamedTypeSymbol delegateSymbol)
        {
            return _expressionsByConvertedType.Value.TryGetValue(delegateSymbol, out var bindings)
              ? bindings
              : Array.Empty<ExpressionBinding>();
        }

        // 返回解析到指定目标类型符号的 TypeSyntax 绑定列表。
        public IReadOnlyList<TypeSyntaxBinding> GetTypeSyntaxBindings(INamedTypeSymbol targetSymbol)
        {
            return _typeSyntaxesByResolvedSymbol.Value.TryGetValue(targetSymbol, out var bindings)
              ? bindings
              : Array.Empty<TypeSyntaxBinding>();
        }

        private IReadOnlyList<InvocationBinding> BuildInvocationBindings()
        {
            var root = SyntaxTree.GetRoot();
            return root.DescendantNodes()
              .OfType<InvocationExpressionSyntax>()
              .Select(invocation => new InvocationBinding(
                invocation,
                ResolveMethodSymbol(SemanticModel, invocation),
                SemanticModel.GetOperation(invocation, CancellationToken.None) as IInvocationOperation))
              .ToList();
        }

        private IReadOnlyList<ElementAccessBinding> BuildElementAccessBindings()
        {
            var root = SyntaxTree.GetRoot();
            return root.DescendantNodes()
              .OfType<ElementAccessExpressionSyntax>()
              .Select(elementAccess => new ElementAccessBinding(
                elementAccess,
                ResolveIndexerSymbol(SemanticModel, elementAccess),
                SemanticModel.GetOperation(elementAccess, CancellationToken.None) as IPropertyReferenceOperation))
              .ToList();
        }

        private IReadOnlyList<ExpressionBinding> BuildExpressionBindings()
        {
            var root = SyntaxTree.GetRoot();
            return root.DescendantNodes()
              .OfType<ExpressionSyntax>()
              .Select(expression =>
              {
                  var typeInfo = SemanticModel.GetTypeInfo(expression, CancellationToken.None);
                  return new ExpressionBinding(
                    expression,
                    SemanticModel.GetOperation(expression, CancellationToken.None),
                    typeInfo.ConvertedType);
              })
              .ToList();
        }

        private IReadOnlyList<TypeSyntaxBinding> BuildTypeSyntaxBindings()
        {
            var root = SyntaxTree.GetRoot();
            return root.DescendantNodes()
              .OfType<TypeSyntax>()
              .Select(typeSyntax => new TypeSyntaxBinding(
                typeSyntax,
                SemanticModel.GetSymbolInfo(typeSyntax, CancellationToken.None).Symbol))
              .ToList();
        }

        private static Lazy<T> CreateLazy<T>(Func<T> factory)
        {
            return new Lazy<T>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private static Dictionary<IMethodSymbol, IReadOnlyList<InvocationBinding>> BuildInvocationIndex(IEnumerable<InvocationBinding> invocationBindings)
        {
            var index = new Dictionary<IMethodSymbol, List<InvocationBinding>>(
              SymbolEqualityComparer.Default);
            foreach (var binding in invocationBindings)
            {
                if (binding.MethodSymbol is null)
                {
                    continue;
                }

                foreach (var lookupSymbol in GetMethodLookupSymbols(binding.MethodSymbol))
                {
                    AddIndexValue(index, lookupSymbol, binding);
                }
            }

            return FreezeIndex(index, SymbolEqualityComparer.Default);
        }

        private static Dictionary<IPropertySymbol, IReadOnlyList<ElementAccessBinding>> BuildElementAccessIndex(IEnumerable<ElementAccessBinding> elementAccessBindings)
        {
            var index = new Dictionary<IPropertySymbol, List<ElementAccessBinding>>(
              SymbolEqualityComparer.Default);
            foreach (var binding in elementAccessBindings)
            {
                if (binding.PropertySymbol is null)
                {
                    continue;
                }

                foreach (var lookupSymbol in GetPropertyLookupSymbols(binding.PropertySymbol))
                {
                    AddIndexValue(index, lookupSymbol, binding);
                }
            }

            return FreezeIndex(index, SymbolEqualityComparer.Default);
        }

        private static Dictionary<INamedTypeSymbol, IReadOnlyList<ExpressionBinding>> BuildExpressionIndex(IEnumerable<ExpressionBinding> expressionBindings)
        {
            var index = new Dictionary<INamedTypeSymbol, List<ExpressionBinding>>(
              SymbolEqualityComparer.Default);
            foreach (var binding in expressionBindings)
            {
                if (binding.ConvertedType is not INamedTypeSymbol convertedType)
                {
                    continue;
                }

                AddIndexValue(index, convertedType, binding);
            }

            return FreezeIndex(index, SymbolEqualityComparer.Default);
        }

        private static Dictionary<INamedTypeSymbol, IReadOnlyList<TypeSyntaxBinding>> BuildTypeSyntaxIndex(IEnumerable<TypeSyntaxBinding> typeSyntaxBindings)
        {
            var index = new Dictionary<INamedTypeSymbol, List<TypeSyntaxBinding>>(
              SymbolEqualityComparer.Default);
            foreach (var binding in typeSyntaxBindings)
            {
                if (binding.Symbol is not INamedTypeSymbol namedType)
                {
                    continue;
                }

                AddIndexValue(index, namedType, binding);
            }

            return FreezeIndex(index, SymbolEqualityComparer.Default);
        }

        private static void AddIndexValue<TKey, TValue>(IDictionary<TKey, List<TValue>> index, TKey key, TValue value)
          where TKey : notnull
        {
            if (!index.TryGetValue(key, out var values))
            {
                values = new List<TValue>();
                index[key] = values;
            }

            values.Add(value);
        }

        private static Dictionary<TKey, IReadOnlyList<TValue>> FreezeIndex<TKey, TValue>(IDictionary<TKey, List<TValue>> index, IEqualityComparer<TKey> comparer)
          where TKey : notnull
        {
            var result = new Dictionary<TKey, IReadOnlyList<TValue>>(index.Count, comparer);
            foreach (var pair in index)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }
    }

    private sealed record InvocationBinding(
      InvocationExpressionSyntax Invocation,
      IMethodSymbol? MethodSymbol,
      IInvocationOperation? Operation);

    private sealed record ElementAccessBinding(
      ElementAccessExpressionSyntax ElementAccess,
      IPropertySymbol? PropertySymbol,
      IPropertyReferenceOperation? Operation);

    private sealed record ExpressionBinding(
      ExpressionSyntax Expression,
      IOperation? Operation,
      ITypeSymbol? ConvertedType);

    private sealed record TypeSyntaxBinding(
      TypeSyntax TypeSyntax,
      ISymbol? Symbol);
}
