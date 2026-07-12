using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassExtensionMethodMappedCallsitePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.ExtensionMethodMappedCallsitePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class extension-method parameter usage to mapped extension callsites";

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
                    "Extension method non-receiver parameter type references the delete-class target; propagate to the owning method declaration."),
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
                    "Extension method invocation passes the delete-class typed parameter; propagate to a shrinkable mapped callsite."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out ExtensionMethodMappedCallsitePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
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
