using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynPrototype.Decision;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Rules;

internal sealed class DeleteClassParameterShrinkAnalyzer
{
    private static readonly ConditionalWeakTable<Compilation, CompilationScanCache> CompilationScanCaches = new();

    internal bool TryBuildNamedArgumentMethodPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out PrivateMethodParameterShrinkPlan plan)
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
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterSymbol,
              out var invocationRewrites))
        {
            return false;
        }

        plan = new PrivateMethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    internal bool TryBuildOptionalParameterMethodPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out PrivateMethodParameterShrinkPlan plan)
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
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterSymbol,
              requireCallsites: DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(method),
              out var invocationRewrites))
        {
            return false;
        }

        plan = new PrivateMethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    internal bool TryBuildParamsMethodPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out PrivateMethodParameterShrinkPlan plan)
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
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterSymbol,
              requireCallsites: DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(method),
              out var invocationRewrites))
        {
            return false;
        }

        plan = new PrivateMethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    internal bool TryBuildPrivateMethodPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out PrivateMethodParameterShrinkPlan plan)
    {
        return TryBuildMethodPlan(
          context,
          typeSyntax,
          DeleteClassMethodProposalSafety.IsSafePrivateMethod,
          requireCallsites: false,
          out plan);
    }

    internal bool TryBuildPublicMethodPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out PublicMethodParameterShrinkPlan plan)
    {
        var succeeded = TryBuildMethodPlan(
          context,
          typeSyntax,
          DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod,
          requireCallsites: true,
          out var methodPlan);
        plan = succeeded
          ? new PublicMethodParameterShrinkPlan(
            methodPlan.Method,
            methodPlan.ReplacementMethod,
            methodPlan.InvocationRewrites)
          : null!;
        return succeeded;
    }

    internal bool TryBuildLocalFunctionPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out LocalFunctionParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveLocalFunctionParameter(typeSyntax, out var localFunction, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(localFunction, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementLocalFunction(localFunction, parameter, out var replacementLocalFunction) ||
            !TryCollectInvocationRewrites(
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

    internal bool TryBuildNamedArgumentLocalFunctionPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out LocalFunctionParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveLocalFunctionParameter(typeSyntax, out var localFunction, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(localFunction, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementLocalFunction(localFunction, parameter, out var replacementLocalFunction) ||
            !TryCollectNamedArgumentInvocationRewrites(
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

    internal bool TryBuildOptionalParameterLocalFunctionPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out LocalFunctionParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveLocalFunctionParameter(typeSyntax, out var localFunction, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(localFunction, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            !IsOptionalParameter(parameter, methodSymbol.Parameters[parameterIndex]) ||
            HasUnsupportedOptionalParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementLocalFunction(localFunction, parameter, out var replacementLocalFunction) ||
            !TryCollectOptionalInvocationRewrites(
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

    internal bool TryBuildIndexerPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out IndexerParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveIndexerParameter(typeSyntax, out var indexer, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(indexer, CancellationToken.None) is not IPropertySymbol indexerSymbol ||
            parameterIndex >= indexerSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, indexerSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementIndexer(indexer, parameter, out var replacementIndexer) ||
            !TryCollectElementAccessRewrites(
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

    internal bool TryBuildNamedArgumentIndexerPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out IndexerParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveIndexerParameter(typeSyntax, out var indexer, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(indexer, CancellationToken.None) is not IPropertySymbol indexerSymbol ||
            parameterIndex >= indexerSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, indexerSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementIndexer(indexer, parameter, out var replacementIndexer) ||
            !TryCollectNamedElementAccessRewrites(
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

    internal bool TryBuildDelegatePlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out DelegateParameterShrinkPlan plan)
    {
        plan = null!;
        var invokeMethod = default(IMethodSymbol);

        if (!TryResolveDelegateParameter(typeSyntax, out var delegateDeclaration, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(delegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
            (invokeMethod = delegateSymbol.DelegateInvokeMethod) is null ||
            parameterIndex >= invokeMethod.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, invokeMethod.Parameters[parameterIndex]) ||
            !TryBuildReplacementDelegate(delegateDeclaration, parameter, out var replacementDelegate) ||
            HasDelegateReferences(context.SemanticModel.Compilation, delegateSymbol))
        {
            return false;
        }

        plan = new DelegateParameterShrinkPlan(delegateDeclaration, replacementDelegate);
        return true;
    }

    internal bool TryBuildDelegateMethodGroupPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out DelegateComplexShrinkPlan plan)
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
          usageSummary.MethodRewrites,
          usageSummary.LocalFunctionRewrites,
          usageSummary.LambdaRewrites,
          usageSummary.InvocationRewrites);
        return true;
    }

    internal bool TryBuildDelegateLambdaPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out DelegateComplexShrinkPlan plan)
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
          usageSummary.MethodRewrites,
          usageSummary.LocalFunctionRewrites,
          usageSummary.LambdaRewrites,
          usageSummary.InvocationRewrites);
        return true;
    }

    internal bool TryBuildDelegateInvocationChainPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out DelegateComplexShrinkPlan plan)
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
          usageSummary.MethodRewrites,
          usageSummary.LocalFunctionRewrites,
          usageSummary.LambdaRewrites,
          usageSummary.InvocationRewrites);
        return true;
    }

    internal bool TryBuildExtensionReceiverNonFirstParameterPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      out PrivateMethodParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveMethodParameter(
              typeSyntax,
              DeleteClassMethodProposalSafety.IsSafeExtensionReceiverMethod,
              out var method,
              out var parameter,
              out var parameterIndex) ||
            parameterIndex <= 0 ||
            method.ParameterList.Parameters.FirstOrDefault() is not ParameterSyntax receiverParameter ||
            !receiverParameter.Modifiers.Any(SyntaxKind.ThisKeyword) ||
            context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            HasConflictingReplacementOverload(methodSymbol, parameterIndex) ||
            !TryBuildReplacementMethod(method, parameter, out var replacementMethod) ||
            !TryCollectMappedInvocationRewrites(
              context.SemanticModel.Compilation,
              methodSymbol,
              methodSymbol.Parameters[parameterIndex],
              requireCallsites: true,
              out var invocationRewrites))
        {
            return false;
        }

        plan = new PrivateMethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    private static bool TryBuildMethodPlan(
      RuleContext context,
      TypeSyntax typeSyntax,
      Func<MethodDeclarationSyntax, bool> methodGuard,
      bool requireCallsites,
      out PrivateMethodParameterShrinkPlan plan)
    {
        plan = null!;

        if (!TryResolveMethodParameter(typeSyntax, methodGuard, out var method, out var parameter, out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            parameterIndex >= methodSymbol.Parameters.Length ||
            HasUnsupportedParameterShape(parameter, methodSymbol.Parameters[parameterIndex]) ||
            !TryBuildReplacementMethod(method, parameter, out var replacementMethod) ||
            !TryCollectInvocationRewrites(
              context.SemanticModel.Compilation,
              methodSymbol,
              parameterIndex,
              method.ParameterList.Parameters.Count,
              requireCallsites,
              out var invocationRewrites))
        {
            return false;
        }

        plan = new PrivateMethodParameterShrinkPlan(method, replacementMethod, invocationRewrites);
        return true;
    }

    private static bool TryResolveMethodParameter(
      TypeSyntax typeSyntax,
      Func<MethodDeclarationSyntax, bool> methodGuard,
      out MethodDeclarationSyntax method,
      out ParameterSyntax parameter,
      out int parameterIndex)
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

    private static bool TryResolveSupportedMethodParameter(
      RuleContext context,
      TypeSyntax typeSyntax,
      out MethodDeclarationSyntax method,
      out IMethodSymbol methodSymbol,
      out ParameterSyntax parameter,
      out IParameterSymbol parameterSymbol,
      out int parameterIndex)
    {
        method = null!;
        methodSymbol = null!;
        parameter = null!;
        parameterSymbol = null!;
        parameterIndex = -1;

        if (!TryResolveMethodParameter(
              typeSyntax,
              DeleteClassMethodProposalSafety.IsSafePrivateMethod,
              out method,
              out parameter,
              out parameterIndex) &&
            !TryResolveMethodParameter(
              typeSyntax,
              DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod,
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

    internal static bool TryResolveLocalFunctionParameter(
      TypeSyntax typeSyntax,
      out LocalFunctionStatementSyntax localFunction,
      out ParameterSyntax parameter,
      out int parameterIndex)
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

    internal static bool TryResolveIndexerParameter(
      TypeSyntax typeSyntax,
      out IndexerDeclarationSyntax indexer,
      out ParameterSyntax parameter,
      out int parameterIndex)
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

    internal static bool TryResolveDelegateParameter(
      TypeSyntax typeSyntax,
      out DelegateDeclarationSyntax delegateDeclaration,
      out ParameterSyntax parameter,
      out int parameterIndex)
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

    internal static bool TryCollectDelegateUsageSummary(
      RuleContext context,
      INamedTypeSymbol delegateSymbol,
      IParameterSymbol parameterSymbol,
      int parameterIndex,
      out DelegateUsageSummary usageSummary)
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

        Parallel.ForEach(
          GetTreeScans(context.SemanticModel.Compilation),
          (scan, state) =>
          {
              if (Volatile.Read(ref failed) != 0)
              {
                  state.Stop();
                  return;
              }

              foreach (var invocation in scan.Invocations)
              {
                  if (!TryResolveMethodSymbol(scan.SemanticModel, invocation, out var targetMethod) ||
                      targetMethod.MethodKind != MethodKind.DelegateInvoke ||
                      !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, delegateSymbol))
                  {
                      continue;
                  }

                  if (!handledInvocations.TryAdd(BuildSyntaxKey(invocation), 0) ||
                      !TryBuildMappedInvocationReplacement(
                        invocation,
                        scan.SemanticModel.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                        parameterSymbol,
                        out var replacementInvocation))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      state.Stop();
                      return;
                  }

                  invocationRewrites.Add(new InvocationRewrite(invocation, replacementInvocation));
              }

              foreach (var expression in scan.Expressions)
              {
                  if (!ExpressionConvertsToDelegate(scan.SemanticModel, expression, delegateSymbol))
                  {
                      continue;
                  }

                  var operation = scan.SemanticModel.GetOperation(expression, CancellationToken.None);
                  switch (operation)
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
                              state.Stop();
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
                          var key = $"{scan.SyntaxTree.FilePath}:{expression.SpanStart}:{expression.Span.Length}";
                          if (!handledLambdaSpans.TryAdd(key, 0) ||
                              !TryBuildLambdaRewrite(
                                context,
                                scan.SemanticModel,
                                expression,
                                anonymousFunction,
                                parameterIndex,
                                out var lambdaRewrite))
                          {
                              Interlocked.Exchange(ref failed, 1);
                              state.Stop();
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

    private static bool HasUnsupportedParameterShape(
      ParameterSyntax parameter,
      IParameterSymbol parameterSymbol)
    {
        return parameter.Default is not null ||
          parameter.Modifiers.Any(SyntaxKind.ParamsKeyword) ||
          parameter.Modifiers.Any(SyntaxKind.ThisKeyword) ||
          parameterSymbol.IsOptional ||
          parameterSymbol.IsParams;
    }

    private static bool HasUnsupportedOptionalParameterShape(
      ParameterSyntax parameter,
      IParameterSymbol parameterSymbol)
    {
        return parameter.Modifiers.Any(SyntaxKind.ParamsKeyword) ||
          parameter.Modifiers.Any(SyntaxKind.ThisKeyword) ||
          parameterSymbol.IsParams;
    }

    private static bool IsOptionalParameter(
      ParameterSyntax parameter,
      IParameterSymbol parameterSymbol)
    {
        return parameter.Default is not null || parameterSymbol.IsOptional;
    }

    private static bool IsParamsParameter(
      ParameterSyntax parameter,
      IParameterSymbol parameterSymbol)
    {
        return parameter.Modifiers.Any(SyntaxKind.ParamsKeyword) || parameterSymbol.IsParams;
    }

    internal static bool TryBuildReplacementMethod(
      MethodDeclarationSyntax method,
      ParameterSyntax parameter,
      out MethodDeclarationSyntax replacementMethod)
    {
        var replacementParameters = method.ParameterList.Parameters.Remove(parameter);
        replacementMethod = method.WithParameterList(method.ParameterList.WithParameters(replacementParameters));
        return replacementParameters.Count + 1 == method.ParameterList.Parameters.Count;
    }

    internal static bool TryBuildReplacementLocalFunction(
      LocalFunctionStatementSyntax localFunction,
      ParameterSyntax parameter,
      out LocalFunctionStatementSyntax replacementLocalFunction)
    {
        var replacementParameters = localFunction.ParameterList.Parameters.Remove(parameter);
        replacementLocalFunction = localFunction.WithParameterList(
          localFunction.ParameterList.WithParameters(replacementParameters));
        return replacementParameters.Count + 1 == localFunction.ParameterList.Parameters.Count;
    }

    internal static bool TryBuildReplacementIndexer(
      IndexerDeclarationSyntax indexer,
      ParameterSyntax parameter,
      out IndexerDeclarationSyntax replacementIndexer)
    {
        var replacementParameters = indexer.ParameterList.Parameters.Remove(parameter);
        replacementIndexer = indexer.WithParameterList(indexer.ParameterList.WithParameters(replacementParameters));
        return replacementParameters.Count + 1 == indexer.ParameterList.Parameters.Count;
    }

    internal static bool TryBuildReplacementDelegate(
      DelegateDeclarationSyntax delegateDeclaration,
      ParameterSyntax parameter,
      out DelegateDeclarationSyntax replacementDelegate)
    {
        var replacementParameters = delegateDeclaration.ParameterList.Parameters.Remove(parameter);
        replacementDelegate = delegateDeclaration.WithParameterList(
          delegateDeclaration.ParameterList.WithParameters(replacementParameters));
        return replacementParameters.Count + 1 == delegateDeclaration.ParameterList.Parameters.Count;
    }

    private static bool TryCollectInvocationRewrites(
      Compilation compilation,
      IMethodSymbol methodSymbol,
      int parameterIndex,
      int expectedParameterCount,
      bool requireCallsites,
      out List<InvocationRewrite> invocationRewrites)
    {
        var rewrites = new ConcurrentBag<InvocationRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        Parallel.ForEach(
          GetTreeScans(compilation),
          (scan, state) =>
          {
              foreach (var invocation in scan.Invocations)
              {
                  if (!TryResolveMethodSymbol(scan.SemanticModel, invocation, out var targetMethod) ||
                      !SymbolEqualityComparer.Default.Equals(methodSymbol, targetMethod))
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildReplacementInvocation(invocation, parameterIndex, expectedParameterCount, out var replacementInvocation))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      state.Stop();
                      return;
                  }

                  rewrites.Add(new InvocationRewrite(invocation, replacementInvocation));
              }
          });

        invocationRewrites = Volatile.Read(ref failed) != 0
          ? new List<InvocationRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryCollectNamedArgumentInvocationRewrites(
      Compilation compilation,
      IMethodSymbol methodSymbol,
      IParameterSymbol parameterSymbol,
      out List<InvocationRewrite> invocationRewrites)
    {
        var rewrites = new ConcurrentBag<InvocationRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        Parallel.ForEach(
          GetTreeScans(compilation),
          (scan, state) =>
          {
              foreach (var invocation in scan.Invocations)
              {
                  if (!TryResolveMethodSymbol(scan.SemanticModel, invocation, out var targetMethod) ||
                      !SymbolEqualityComparer.Default.Equals(methodSymbol, targetMethod) ||
                      scan.SemanticModel.GetOperation(invocation, CancellationToken.None) is not IInvocationOperation invocationOperation)
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildNamedArgumentReplacementInvocation(
                        invocation,
                        invocationOperation,
                        parameterSymbol,
                        out var replacementInvocation))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      state.Stop();
                      return;
                  }

                  rewrites.Add(new InvocationRewrite(invocation, replacementInvocation));
              }
          });

        invocationRewrites = Volatile.Read(ref failed) != 0
          ? new List<InvocationRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          Volatile.Read(ref matchedCallsites) > 0;
    }

    private static bool TryCollectOptionalInvocationRewrites(
      Compilation compilation,
      IMethodSymbol methodSymbol,
      IParameterSymbol parameterSymbol,
      bool requireCallsites,
      out List<InvocationRewrite> invocationRewrites)
    {
        var matchedCallsites = 0;
        var rewrites = new ConcurrentBag<InvocationRewrite>();
        var failed = 0;

        Parallel.ForEach(
          GetTreeScans(compilation),
          (scan, state) =>
          {
              foreach (var invocation in scan.Invocations)
              {
                  if (!TryResolveMethodSymbol(scan.SemanticModel, invocation, out var targetMethod) ||
                      !SymbolEqualityComparer.Default.Equals(methodSymbol, targetMethod) ||
                      scan.SemanticModel.GetOperation(invocation, CancellationToken.None) is not IInvocationOperation invocationOperation)
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildOptionalReplacementInvocation(
                        invocation,
                        invocationOperation,
                        parameterSymbol,
                        out var replacementInvocation,
                        out var changed))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      state.Stop();
                      return;
                  }

                  if (changed)
                  {
                      rewrites.Add(new InvocationRewrite(invocation, replacementInvocation));
                  }
              }
          });

        invocationRewrites = Volatile.Read(ref failed) != 0
          ? new List<InvocationRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryCollectParamsInvocationRewrites(
      Compilation compilation,
      IMethodSymbol methodSymbol,
      IParameterSymbol parameterSymbol,
      bool requireCallsites,
      out List<InvocationRewrite> invocationRewrites)
    {
        invocationRewrites = new List<InvocationRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        Parallel.ForEach(
          GetTreeScans(compilation),
          (scan, state) =>
          {
              foreach (var invocation in scan.Invocations)
              {
                  if (!TryResolveMethodSymbol(scan.SemanticModel, invocation, out var targetMethod) ||
                      !SymbolEqualityComparer.Default.Equals(methodSymbol, targetMethod) ||
                      scan.SemanticModel.GetOperation(invocation, CancellationToken.None) is not IInvocationOperation invocationOperation)
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
                      state.Stop();
                      return;
                  }
              }
          });

        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryCollectElementAccessRewrites(
      Compilation compilation,
      IPropertySymbol indexerSymbol,
      int parameterIndex,
      int expectedParameterCount,
      bool requireCallsites,
      out List<ElementAccessRewrite> accessRewrites)
    {
        var rewrites = new ConcurrentBag<ElementAccessRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        Parallel.ForEach(
          GetTreeScans(compilation),
          (scan, state) =>
          {
              foreach (var elementAccess in scan.ElementAccesses)
              {
                  if (!TryResolveIndexerSymbol(scan.SemanticModel, elementAccess, out var targetIndexer) ||
                      !SymbolEqualityComparer.Default.Equals(indexerSymbol, targetIndexer))
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildReplacementElementAccess(
                        elementAccess,
                        parameterIndex,
                        expectedParameterCount,
                        out var replacementElementAccess))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      state.Stop();
                      return;
                  }

                  rewrites.Add(new ElementAccessRewrite(elementAccess, replacementElementAccess));
              }
          });

        accessRewrites = Volatile.Read(ref failed) != 0
          ? new List<ElementAccessRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.ElementAccess), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryCollectNamedElementAccessRewrites(
      Compilation compilation,
      IPropertySymbol indexerSymbol,
      IParameterSymbol parameterSymbol,
      out List<ElementAccessRewrite> accessRewrites)
    {
        var rewrites = new ConcurrentBag<ElementAccessRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        Parallel.ForEach(
          GetTreeScans(compilation),
          (scan, state) =>
          {
              foreach (var elementAccess in scan.ElementAccesses)
              {
                  if (!TryResolveIndexerSymbol(scan.SemanticModel, elementAccess, out var targetIndexer) ||
                      !SymbolEqualityComparer.Default.Equals(indexerSymbol, targetIndexer) ||
                      scan.SemanticModel.GetOperation(elementAccess, CancellationToken.None) is not IPropertyReferenceOperation propertyReference)
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildNamedArgumentReplacementElementAccess(
                        elementAccess,
                        propertyReference,
                        parameterSymbol,
                        out var replacementElementAccess))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      state.Stop();
                      return;
                  }

                  rewrites.Add(new ElementAccessRewrite(elementAccess, replacementElementAccess));
              }
          });

        accessRewrites = Volatile.Read(ref failed) != 0
          ? new List<ElementAccessRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.ElementAccess), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          Volatile.Read(ref matchedCallsites) > 0;
    }

    private static bool TryCollectMappedInvocationRewrites(
      Compilation compilation,
      IMethodSymbol methodSymbol,
      IParameterSymbol parameterSymbol,
      bool requireCallsites,
      out List<InvocationRewrite> invocationRewrites)
    {
        var rewrites = new ConcurrentBag<InvocationRewrite>();
        var matchedCallsites = 0;
        var failed = 0;

        Parallel.ForEach(
          GetTreeScans(compilation),
          (scan, state) =>
          {
              foreach (var invocation in scan.Invocations)
              {
                  if (!TryResolveMethodSymbol(scan.SemanticModel, invocation, out var targetMethod) ||
                      !MethodMatchesInvocationTarget(methodSymbol, targetMethod))
                  {
                      continue;
                  }

                  Interlocked.Increment(ref matchedCallsites);
                  if (!TryBuildMappedInvocationReplacement(
                        invocation,
                        scan.SemanticModel.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                        parameterSymbol,
                        out var replacementInvocation))
                  {
                      Interlocked.Exchange(ref failed, 1);
                      state.Stop();
                      return;
                  }

                  rewrites.Add(new InvocationRewrite(invocation, replacementInvocation));
              }
          });

        invocationRewrites = Volatile.Read(ref failed) != 0
          ? new List<InvocationRewrite>()
          : rewrites.OrderBy(static item => BuildSyntaxKey(item.Invocation), StringComparer.Ordinal).ToList();
        return Volatile.Read(ref failed) == 0 &&
          (!requireCallsites || Volatile.Read(ref matchedCallsites) > 0);
    }

    private static bool TryResolveMethodSymbol(
      SemanticModel semanticModel,
      InvocationExpressionSyntax invocation,
      out IMethodSymbol methodSymbol)
    {
        methodSymbol = semanticModel.GetSymbolInfo(invocation, CancellationToken.None).Symbol as IMethodSymbol
          ?? semanticModel.GetSymbolInfo(invocation, CancellationToken.None)
            .CandidateSymbols
            .OfType<IMethodSymbol>()
            .SingleOrDefault()!;
        return methodSymbol is not null;
    }

    private static bool TryResolveIndexerSymbol(
      SemanticModel semanticModel,
      ElementAccessExpressionSyntax elementAccess,
      out IPropertySymbol indexerSymbol)
    {
        indexerSymbol = semanticModel.GetSymbolInfo(elementAccess, CancellationToken.None).Symbol as IPropertySymbol
          ?? semanticModel.GetSymbolInfo(elementAccess, CancellationToken.None)
            .CandidateSymbols
            .OfType<IPropertySymbol>()
            .SingleOrDefault()!;
        return indexerSymbol is not null;
    }

    private static bool MethodMatchesInvocationTarget(
      IMethodSymbol declaredMethod,
      IMethodSymbol targetMethod)
    {
        return SymbolEqualityComparer.Default.Equals(declaredMethod, targetMethod) ||
          SymbolEqualityComparer.Default.Equals(declaredMethod, targetMethod.ReducedFrom) ||
          SymbolEqualityComparer.Default.Equals(declaredMethod.OriginalDefinition, targetMethod.OriginalDefinition) ||
          (targetMethod.ReducedFrom is not null &&
           SymbolEqualityComparer.Default.Equals(declaredMethod.OriginalDefinition, targetMethod.ReducedFrom.OriginalDefinition));
    }

    private static bool ExpressionConvertsToDelegate(
      SemanticModel semanticModel,
      ExpressionSyntax expression,
      INamedTypeSymbol delegateSymbol)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, CancellationToken.None);
        return SymbolEqualityComparer.Default.Equals(typeInfo.ConvertedType, delegateSymbol);
    }

    internal static bool TryBuildReplacementInvocation(
      InvocationExpressionSyntax invocation,
      int parameterIndex,
      int expectedParameterCount,
      out InvocationExpressionSyntax replacementInvocation)
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

    internal static bool TryBuildNamedArgumentReplacementInvocation(
      InvocationExpressionSyntax invocation,
      IInvocationOperation invocationOperation,
      IParameterSymbol parameterSymbol,
      out InvocationExpressionSyntax replacementInvocation)
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

    internal static bool TryBuildOptionalReplacementInvocation(
      InvocationExpressionSyntax invocation,
      IInvocationOperation invocationOperation,
      IParameterSymbol parameterSymbol,
      out InvocationExpressionSyntax replacementInvocation,
      out bool changed)
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

    internal static bool TryBuildMappedInvocationReplacement(
      InvocationExpressionSyntax invocation,
      IInvocationOperation? invocationOperation,
      IParameterSymbol parameterSymbol,
      out InvocationExpressionSyntax replacementInvocation)
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

    internal static bool TryBuildNamedArgumentReplacementElementAccess(
      ElementAccessExpressionSyntax elementAccess,
      IPropertyReferenceOperation propertyReference,
      IParameterSymbol parameterSymbol,
      out ElementAccessExpressionSyntax replacementElementAccess)
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

    private static bool TryBuildMethodGroupTargetRewrite(
      RuleContext context,
      IMethodSymbol targetMethod,
      int parameterIndex,
      out MethodRewrite? methodRewrite,
      out LocalFunctionRewrite? localFunctionRewrite)
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
            if (!DeleteClassMethodProposalSafety.IsSafePrivateMethod(methodDeclaration) &&
                !DeleteClassMethodProposalSafety.IsSafeNonPrivateMethod(methodDeclaration) &&
                !DeleteClassMethodProposalSafety.IsSafeExtensionReceiverMethod(methodDeclaration))
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

    internal static bool TryBuildLambdaRewrite(
      RuleContext context,
      SemanticModel semanticModel,
      ExpressionSyntax expression,
      IAnonymousFunctionOperation anonymousFunction,
      int parameterIndex,
      out ExpressionRewrite lambdaRewrite)
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

    private static bool TryBuildParenthesizedLambdaRewrite(
      SemanticModel semanticModel,
      ParenthesizedLambdaExpressionSyntax lambda,
      IParameterSymbol parameterSymbol,
      int parameterIndex,
      out ExpressionRewrite rewrite)
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

    private static bool TryBuildSimpleLambdaRewrite(
      SemanticModel semanticModel,
      SimpleLambdaExpressionSyntax lambda,
      IParameterSymbol parameterSymbol,
      int parameterIndex,
      out ExpressionRewrite rewrite)
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

    private static bool TryBuildAnonymousMethodRewrite(
      SemanticModel semanticModel,
      AnonymousMethodExpressionSyntax anonymousMethod,
      IParameterSymbol parameterSymbol,
      int parameterIndex,
      out ExpressionRewrite rewrite)
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

    private static bool IsLambdaParameterUsed(
      SemanticModel semanticModel,
      SyntaxNode lambdaRoot,
      IParameterSymbol parameterSymbol)
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

    internal static bool TryBuildReplacementElementAccess(
      ElementAccessExpressionSyntax elementAccess,
      int parameterIndex,
      int expectedParameterCount,
      out ElementAccessExpressionSyntax replacementElementAccess)
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

    internal static bool HasDelegateReferences(
      Compilation compilation,
      INamedTypeSymbol delegateSymbol)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var scan = GetTreeScan(compilation, tree);
            foreach (var node in scan.TypeSyntaxes)
            {
                var symbol = scan.SemanticModel.GetSymbolInfo(node, CancellationToken.None).Symbol;
                if (SymbolEqualityComparer.Default.Equals(symbol, delegateSymbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<TreeScan> GetTreeScans(Compilation compilation)
    {
        return CompilationScanCaches.GetValue(
          compilation,
          static key => new CompilationScanCache(key)).TreeScans;
    }

    private static TreeScan GetTreeScan(Compilation compilation, SyntaxTree tree)
    {
        return CompilationScanCaches.GetValue(
          compilation,
          static key => new CompilationScanCache(key)).GetTreeScan(tree);
    }

    internal static bool HasConflictingReplacementOverload(
      IMethodSymbol methodSymbol,
      int parameterIndex)
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
        private readonly Dictionary<SyntaxTree, TreeScan> _treeScans;

        public CompilationScanCache(Compilation compilation)
        {
            _treeScans = new Dictionary<SyntaxTree, TreeScan>(ReferenceEqualityComparer.Instance);
            foreach (var tree in compilation.SyntaxTrees)
            {
                _treeScans[tree] = BuildTreeScan(compilation, tree);
            }
            TreeScans = _treeScans.Values.ToList();
        }

        public IReadOnlyList<TreeScan> TreeScans { get; }

        public TreeScan GetTreeScan(SyntaxTree tree)
        {
            return _treeScans[tree];
        }

        private static TreeScan BuildTreeScan(Compilation compilation, SyntaxTree tree)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            return new TreeScan(
              tree,
              semanticModel,
              root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList(),
              root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().ToList(),
              root.DescendantNodes().OfType<ExpressionSyntax>().ToList(),
              root.DescendantNodes().OfType<TypeSyntax>().ToList());
        }
    }

    private sealed record TreeScan(
      SyntaxTree SyntaxTree,
      SemanticModel SemanticModel,
      IReadOnlyList<InvocationExpressionSyntax> Invocations,
      IReadOnlyList<ElementAccessExpressionSyntax> ElementAccesses,
      IReadOnlyList<ExpressionSyntax> Expressions,
      IReadOnlyList<TypeSyntax> TypeSyntaxes);
}

internal sealed record InvocationRewrite(
  InvocationExpressionSyntax Invocation,
  InvocationExpressionSyntax Replacement);

internal sealed record ElementAccessRewrite(
  ElementAccessExpressionSyntax ElementAccess,
  ElementAccessExpressionSyntax Replacement);

internal sealed record PrivateMethodParameterShrinkPlan(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);

internal sealed record PublicMethodParameterShrinkPlan(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);

internal sealed record LocalFunctionParameterShrinkPlan(
  LocalFunctionStatementSyntax LocalFunction,
  LocalFunctionStatementSyntax ReplacementLocalFunction,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);

internal sealed record IndexerParameterShrinkPlan(
  IndexerDeclarationSyntax Indexer,
  IndexerDeclarationSyntax ReplacementIndexer,
  IReadOnlyList<ElementAccessRewrite> AccessRewrites);

internal sealed record DelegateParameterShrinkPlan(
  DelegateDeclarationSyntax DelegateDeclaration,
  DelegateDeclarationSyntax ReplacementDelegate);

internal sealed record MethodRewrite(
  MethodDeclarationSyntax Method,
  MethodDeclarationSyntax ReplacementMethod);

internal sealed record LocalFunctionRewrite(
  LocalFunctionStatementSyntax LocalFunction,
  LocalFunctionStatementSyntax ReplacementLocalFunction);

internal sealed record ExpressionRewrite(
  ExpressionSyntax Expression,
  ExpressionSyntax Replacement);

internal sealed record DelegateUsageSummary(
  IReadOnlyList<MethodRewrite> MethodRewrites,
  IReadOnlyList<LocalFunctionRewrite> LocalFunctionRewrites,
  IReadOnlyList<ExpressionRewrite> LambdaRewrites,
  IReadOnlyList<InvocationRewrite> InvocationRewrites,
  IReadOnlyCollection<string> MethodGroupTargets);

internal sealed record DelegateComplexShrinkPlan(
  DelegateDeclarationSyntax DelegateDeclaration,
  DelegateDeclarationSyntax ReplacementDelegate,
  IReadOnlyList<MethodRewrite> MethodRewrites,
  IReadOnlyList<LocalFunctionRewrite> LocalFunctionRewrites,
  IReadOnlyList<ExpressionRewrite> LambdaRewrites,
  IReadOnlyList<InvocationRewrite> InvocationRewrites);
