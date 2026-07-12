using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassLocalFunctionParameterUsagePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.LocalFunctionParameterUsagePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class local-function parameter usage to local functions and mapped callsites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.LocalFunctionStatement,
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

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.LocalFunction)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    payload.LocalFunction,
                    "Local function parameter type references the delete-class target; propagate to the owning local function."),
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
                    "Local function invocation passes the delete-class typed parameter; propagate to a shrinkable callsite."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out LocalFunctionParameterUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (_analyzer.TryBuildNamedArgumentLocalFunctionPlan(context, typeSyntax, out var namedPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              namedPlan.LocalFunction,
              LocalFunctionParameterUsageMode.NamedArgument,
              namedPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildOptionalParameterLocalFunctionPlan(context, typeSyntax, out var optionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              optionalPlan.LocalFunction,
              LocalFunctionParameterUsageMode.Optional,
              optionalPlan.InvocationRewrites);
            return true;
        }

        if (_analyzer.TryBuildLocalFunctionPlan(context, typeSyntax, out var positionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              positionalPlan.LocalFunction,
              LocalFunctionParameterUsageMode.Positional,
              positionalPlan.InvocationRewrites);
            return true;
        }

        return false;
    }

    private static LocalFunctionParameterUsagePayload CreatePayload(TypeSyntax typeSyntax, LocalFunctionStatementSyntax localFunction, LocalFunctionParameterUsageMode mode, IReadOnlyList<InvocationRewrite> invocationRewrites)
    {
        var parameterIndex = localFunction.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true);
        return new LocalFunctionParameterUsagePayload(
          localFunction,
          parameterIndex.parameter,
          parameterIndex.index,
          mode,
          invocationRewrites.Select(rewrite => rewrite.Invocation).ToList());
    }
}
