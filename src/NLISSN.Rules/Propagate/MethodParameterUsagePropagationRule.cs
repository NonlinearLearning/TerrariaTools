using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 为方法参数删除汇总声明宿主与调用点，并把不同调用约束编码成 mode，供后续收缩提案选择正确改写策略。
public sealed class MethodParameterUsagePropagationRule : RuleDefinitionPropagate
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
    private static readonly RuleFactKind MethodParameterUsageFactKind = RuleFactKind.RelationParameterUsage;

    private static readonly RuleProducesContract MethodParameterUsageProduces =
      new(new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.MethodDeclaration, SyntaxKind.InvocationExpression },
          MethodParameterUsageFactKind)
      });

    private readonly ParameterShrinkAnalyzer _analyzer = new();


    public override string RuleId { get; } = "propagate.type.method-parameter-usage";

    public override RuleProducesContract Produces => MethodParameterUsageProduces;

    public override RuleConsumesContract Consumes => TypeSyntaxFactsConsumes;


    public override string Name { get; } = "Propagate delete-class method parameter usage to owning methods and mapped callsites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    // 把方法参数删除需要的声明与调用点事实编码成 payload，并区分不同调用约束模式。
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
                    "Method parameter type references the delete-class target; propagate to the owning method declaration.",
                    factKind: MethodParameterUsageFactKind),
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
                    "Invocation passes the delete-class typed parameter; propagate to a shrinkable callsite.",
                    factKind: MethodParameterUsageFactKind),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    /// 解析顺序体现保守性：先处理命名参数、默认值和 params 等高约束形状，
    private bool TryBuildPayload(IPropagationRuleContext context, MarkRecord seedMark, out MethodParameterUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, "mark.type.type-syntax", StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (_analyzer.TryBuildNamedArgumentMethodPlan(context, typeSyntax, out var namedPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              namedPlan.Method,
              MethodParameterUsageMode.NamedArgument,
              namedPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildOptionalParameterMethodPlan(context, typeSyntax, out var optionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              optionalPlan.Method,
              MethodParameterUsageMode.Optional,
              optionalPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildParamsMethodPlan(context, typeSyntax, out var paramsPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              paramsPlan.Method,
              MethodParameterUsageMode.ParamsOmitted,
              paramsPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildPrivateMethodPlan(context, typeSyntax, out var privatePlan))
        {
            payload = CreatePayload(
              typeSyntax,
              privatePlan.Method,
              MethodParameterUsageMode.PrivatePositional,
              privatePlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildPublicMethodPlan(context, typeSyntax, out var publicPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              publicPlan.Method,
              MethodParameterUsageMode.PublicPositional,
              publicPlan.InvocationRewrites);
            return true;
        }

        return false;
    }

    private static MethodParameterUsagePayload CreatePayload(TypeSyntax typeSyntax, MethodDeclarationSyntax method, MethodParameterUsageMode mode, IReadOnlyList<InvocationRewrite> invocationRewrites)
    {
        var parameterIndex = method.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true);
        return new MethodParameterUsagePayload(
          method,
          parameterIndex.parameter,
          parameterIndex.index,
          mode,
          invocationRewrites.Select(rewrite => rewrite.Invocation).ToList());
    }
}
