using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 为扩展方法的非接收者参数收集声明与映射调用点，
/// 保持 receiver 绑定不变，只把可安全收缩的槽位继续传给提案阶段。
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class ExtensionMethodMappedCallsitePropagationRule : RuleDefinitionPropagate
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
        RuleFactKind.TargetTypeSyntax)
    });
    private static readonly RuleFactKind ExtensionMethodParameterUsageFactKind = RuleFactKind.RelationExtensionUsage;

    private static readonly RuleProducesContract ExtensionMethodParameterUsageProduces =
      new(new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.MethodDeclaration, SyntaxKind.InvocationExpression },
          ExtensionMethodParameterUsageFactKind)
      });

    private readonly ParameterShrinkAnalyzer _analyzer = new();


    public override string RuleId { get; } = "propagate.type.extension-method-mapped-callsite";

    public override RuleProducesContract Produces => ExtensionMethodParameterUsageProduces;

    public override RuleConsumesContract Consumes => TypeSyntaxFactsConsumes;


    public override string Name { get; } = "Propagate delete-class extension-method parameter usage to mapped extension callsites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    // 把可安全收缩的扩展方法参数传播到方法声明和映射调用点，同时保持接收者绑定不变。
    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!TryBuildPayload(context, seedMark, out var payload))
            {
                continue;
            }

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.Method)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                  RuleId,
                  payload.Method,
                  "Extension method non-receiver parameter type references the delete-class target; propagate to the owning method declaration.",
                  factKind: ExtensionMethodParameterUsageFactKind),
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
                    "Extension method invocation passes the delete-class typed parameter; propagate to a shrinkable mapped callsite.",
                    factKind: ExtensionMethodParameterUsageFactKind),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    /// 这里只接受 analyzer 已证明“接收者不变、非首参可收缩”的情况，
    private bool TryBuildPayload(IPropagationRuleContext context, MarkRecord seedMark, out ExtensionMethodMappedCallsitePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, "mark.type.type-syntax", StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax ||
            !_analyzer.TryBuildExtensionReceiverNonFirstParameterPlan(context, typeSyntax, out var plan))
        {
            return false;
        }

        var parameterIndex = plan.Method.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true)
          .index;
        payload = new ExtensionMethodMappedCallsitePayload(
          plan.Method,
          plan.Method.ParameterList.Parameters[parameterIndex],
          parameterIndex,
          plan.InvocationRewrites.Select(rewrite => rewrite.Invocation).ToList());
        return true;
    }
}
