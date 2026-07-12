using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassIndexerParameterUsagePropagationRule : RuleDefinitionPropagate
{
    private readonly DeleteClassParameterShrinkAnalyzer _analyzer = new();

    public override string RuleId { get; } = DeleteClassRuleIds.IndexerParameterUsagePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class indexer parameter usage to indexers and mapped access sites";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
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

            if (knownKeys.Add(DecisionCpgFactory.BuildNodeKey(payload.Indexer)))
            {
                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    payload.Indexer,
                    "Indexer parameter type references the delete-class target; propagate to the owning indexer declaration."),
                  seedMark,
                  1,
                  Payload: payload);
            }

            foreach (var access in payload.AccessCallsites)
            {
                if (!knownKeys.Add(DecisionCpgFactory.BuildNodeKey(access)))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  MarkRecordFactory.Create(
                    RuleId,
                    access,
                    "Indexer access passes the delete-class typed parameter; propagate to a shrinkable access site."),
                  seedMark,
                  1,
                  Payload: payload);
            }
        }
    }

    private bool TryBuildPayload(RuleContext context, MarkRecord seedMark, out IndexerParameterUsagePayload payload)
    {
        payload = null!;
        if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
            seedMark.SyntaxNode is not TypeSyntax typeSyntax)
        {
            return false;
        }

        if (_analyzer.TryBuildNamedArgumentIndexerPlan(context, typeSyntax, out var namedPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              namedPlan.Indexer,
              IndexerParameterUsageMode.NamedArgument,
              namedPlan.AccessRewrites);
            return true;
        }

        if (_analyzer.TryBuildIndexerPlan(context, typeSyntax, out var positionalPlan))
        {
            payload = CreatePayload(
              typeSyntax,
              positionalPlan.Indexer,
              IndexerParameterUsageMode.Positional,
              positionalPlan.AccessRewrites);
            return true;
        }

        return false;
    }

    private static IndexerParameterUsagePayload CreatePayload(TypeSyntax typeSyntax, IndexerDeclarationSyntax indexer, IndexerParameterUsageMode mode, IReadOnlyList<ElementAccessRewrite> accessRewrites)
    {
        var parameterIndex = indexer.ParameterList.Parameters
          .Select((parameter, index) => new { parameter, index })
          .First(item => item.parameter.Type?.Span.Contains(typeSyntax.Span) == true);
        return new IndexerParameterUsagePayload(
          indexer,
          parameterIndex.parameter,
          parameterIndex.index,
          mode,
          accessRewrites.Select(rewrite => rewrite.ElementAccess).ToList());
    }
}
