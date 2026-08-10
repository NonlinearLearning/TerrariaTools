using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class NamedArgumentIndexerParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{
    public override string CapabilityId { get; } = "propose.type.named-argument-indexer-parameter-shrink";

    public override string RuleId { get; } = "DEL-CLASS-PROP-NAMED-INDEXER-PARAM-SHRINK-001";


    public override string Name { get; } = "Shrink indexer parameters whose type references the delete-class target when accesses use named arguments";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ElementAccessExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 针对命名索引实参同步收缩索引器声明与 element access。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in IndexerUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     IndexerParameterUsageMode.NamedArgument))
        {
            if (!IndexerUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementIndexer))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateIndexerReplaceDecision(
              RuleId,
              payload.Indexer,
              replacementIndexer,
              "Indexer parameter type references the delete-class target; shrink the signature for named-argument accesses.");

            foreach (var decision in IndexerUsageProposalHelpers.CreateAccessReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Indexer access passes the deleted class type value by name; remove the matching named argument."))
            {
                yield return decision;
            }
        }
    }
}

