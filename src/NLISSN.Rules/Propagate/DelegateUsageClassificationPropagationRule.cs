using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 先把 delegate 参数删除影响分类成单一改写通道，再把同一份 payload 发给声明、method group、lambda 或调用链。
public sealed class DelegateUsageClassificationPropagationRule : RuleDefinitionPropagate
{
    private static readonly RuleConsumesContract TypeSyntaxFactsConsumes = new(new[]
    {
      new RuleConsumedSyntax(
        new[]
        {
          SyntaxKind.IdentifierName,
          SyntaxKind.QualifiedName,
          SyntaxKind.AliasQualifiedName,
          SyntaxKind.GenericName
        },
        RuleFactPorts.TargetTypeSyntax)
    });
    private static readonly RuleSemanticTag DelegateUsageSemanticTag = RuleFactPorts.RelationDelegateUsage;

    private static readonly RuleProducesContract DelegateUsageProduces =
      new(new[]
      {
        new RuleProducedSyntax(
          new[]
          {
            SyntaxKind.DelegateDeclaration,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.AnonymousMethodExpression,
            SyntaxKind.InvocationExpression
          },
          DelegateUsageSemanticTag)
      });

    private readonly ParameterShrinkAnalyzer _analyzer = new();

    public override string CapabilityId { get; } = "propagate.type.delegate-usage-classification";

    public override string RuleId { get; } = "DEL-CLASS-PROP-DELEGATE-USAGE-001";

    public override RuleProducesContract Produces => DelegateUsageProduces;

    public override RuleConsumesContract Consumes => TypeSyntaxFactsConsumes;


    public override string Name { get; } = "Propagate delete-class delegate parameter usage to delegate declarations and mapped bindings";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.MethodDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.ParenthesizedLambdaExpression,
        SyntaxKind.SimpleLambdaExpression,
        SyntaxKind.AnonymousMethodExpression,
        SyntaxKind.InvocationExpression
      };

    // 先把委托参数删除归类成单一使用模式，再把统一 payload 发给声明和受影响绑定。
    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(context, seedMark, out var payload))
            {
                continue;
            }

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.DelegateDeclaration)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                  RuleId,
                  payload.DelegateDeclaration,
                  "Delegate parameter type references the delete-class target; propagate to the owning delegate declaration.",
                  semanticTag: DelegateUsageSemanticTag),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var method in payload.MethodTargets)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(method)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    method,
                    "Delegate method-group target must shrink to stay compatible with the delete-class delegate signature.",
                    semanticTag: DelegateUsageSemanticTag),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var localFunction in payload.LocalFunctionTargets)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(localFunction)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    localFunction,
                    "Delegate local-function target must shrink to stay compatible with the delete-class delegate signature.",
                    semanticTag: DelegateUsageSemanticTag),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var lambda in payload.LambdaTargets)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(lambda)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    lambda,
                    "Delegate lambda binding must shrink to stay compatible with the delete-class delegate signature.",
                    semanticTag: DelegateUsageSemanticTag),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(invocation)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    invocation,
                    "Delegate invocation passes the delete-class typed parameter; propagate to a shrinkable invocation chain.",
                    semanticTag: DelegateUsageSemanticTag),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    /// 混合 method group / lambda / 调用链会引入不同重绑定风险，
    private bool TryBuildPayload(IPropagationRuleContext context, MarkRecord seedMark, out DelegateUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, "DEL-CLASS-MARK-TYPE-001", StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax ||
            !ParameterShrinkAnalyzer.TryResolveDelegateParameter(
              typeSyntax,
              out var delegateDeclaration,
              out var parameter,
              out var parameterIndex) ||
            context.SemanticModel.GetDeclaredSymbol(delegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
            delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
            parameterIndex >= invokeMethod.Parameters.Length ||
            !ParameterShrinkAnalyzer.TryBuildReplacementDelegate(
              delegateDeclaration,
              parameter,
              out _))
        {
            return false;
        }

        if (ParameterShrinkAnalyzer.TryCollectDelegateUsageSummary(
              context,
              delegateSymbol,
              invokeMethod.Parameters[parameterIndex],
              parameterIndex,
              out var usageSummary))
        {
            if (usageSummary.MethodGroupTargets.Count > 0 && usageSummary.LambdaRewrites.Count == 0)
            {
                payload = CreatePayload(
                  delegateDeclaration,
                  parameter,
                  parameterIndex,
                  DelegateUsageMode.MethodGroup,
                  usageSummary);
                return true;
            }

            if (usageSummary.LambdaRewrites.Count > 0 && usageSummary.MethodGroupTargets.Count == 0)
            {
                payload = CreatePayload(
                  delegateDeclaration,
                  parameter,
                  parameterIndex,
                  DelegateUsageMode.Lambda,
                  usageSummary);
                return true;
            }

            if (usageSummary.InvocationRewrites.Count > 0 &&
                usageSummary.MethodGroupTargets.Count == 0 &&
                usageSummary.LambdaRewrites.Count == 0)
            {
                payload = CreatePayload(
                  delegateDeclaration,
                  parameter,
                  parameterIndex,
                  DelegateUsageMode.InvocationChain,
                  usageSummary);
                return true;
            }
        }

        if (ParameterShrinkAnalyzer.HasDelegateReferences(
              context.Runtime,
              context.SemanticModel.Compilation,
              delegateSymbol))
        {
            return false;
        }

        if (_analyzer.TryBuildDelegatePlan(context, typeSyntax, out var plainPlan))
        {
            payload = new DelegateUsagePayload(
              plainPlan.DelegateDeclaration,
              parameter,
              parameterIndex,
              DelegateUsageMode.PlainSignature,
              Array.Empty<MethodDeclarationSyntax>(),
              Array.Empty<LocalFunctionStatementSyntax>(),
              Array.Empty<ExpressionSyntax>(),
              Array.Empty<InvocationExpressionSyntax>());
            return true;
        }

        return false;
    }

    private static DelegateUsagePayload CreatePayload(DelegateDeclarationSyntax delegateDeclaration, ParameterSyntax parameter, int parameterIndex, DelegateUsageMode mode, DelegateUsageSummary usageSummary)
    {
        return new DelegateUsagePayload(
          delegateDeclaration,
          parameter,
          parameterIndex,
          mode,
          usageSummary.MethodRewrites.Select(rewrite => rewrite.Method).ToList(),
          usageSummary.LocalFunctionRewrites.Select(rewrite => rewrite.LocalFunction).ToList(),
          usageSummary.LambdaRewrites.Select(rewrite => rewrite.Expression).ToList(),
          usageSummary.InvocationRewrites.Select(rewrite => rewrite.Invocation).ToList());
    }
}
