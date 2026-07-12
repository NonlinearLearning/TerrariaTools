using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassMethodParameterUsagePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.MethodParameterUsagePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class method parameter usage to owning methods and mapped callsites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
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
                    "Method parameter type references the delete-class target; propagate to the owning method declaration."),
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
                    "Invocation passes the delete-class typed parameter; propagate to a shrinkable callsite."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out MethodParameterUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
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
