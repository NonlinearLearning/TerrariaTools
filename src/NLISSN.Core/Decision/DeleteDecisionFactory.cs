using Microsoft.CodeAnalysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Lifting;

namespace NLISSN.Core.Decision;

public static class DeleteDecisionFactory
{
    // 为完整声明边界创建可支配其内部候选的删除决策。
    public static DecisionUnit CreateDeclarationDeleteDecision(
      string ruleId,
      SyntaxNode declarationNode,
      string reason,
      SyntaxNode? sourceNode = null,
      string? conflictKey = null)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return CreateDeleteDecision(
          ruleId,
          declarationNode,
          reason,
          sourceNode,
          conflictKey,
          consumedNodes: declarationNode.DescendantNodesAndSelf(),
          composition: DecisionComposition.OpaqueDominates,
          dominatesChildren: true,
          declarationBoundary: true);
    }

    // 为一个锚点语法节点创建删除决策单元，并在需要时附带来源片段关系。
    public static DecisionUnit CreateDeleteDecision(
      string ruleId,
      SyntaxNode anchorNode,
      string reason,
      SyntaxNode? sourceNode = null,
      string? conflictKey = null,
      IEnumerable<SyntaxNode>? consumedNodes = null,
      CoverageProof? proof = null,
      DecisionComposition composition = DecisionComposition.Unknown,
      bool dominatesChildren = false,
      bool declarationBoundary = false)
    {
        var anchorFragment = CreateFragment(anchorNode, "anchor", DecisionActionKind.Delete);
        var fragments = new List<NLCPG.Model.NLCPGNode> { anchorFragment };
        var relations = new List<NLCPG.Model.NLCPGEdge>();
        var bindings = new List<(NLCPG.Model.NLCPGNode Fragment, SyntaxNode Node)>
        {
          (anchorFragment, anchorNode)
        };

        if (sourceNode is not null && !ReferenceEquals(sourceNode, anchorNode))
        {
            var sourceFragment = CreateFragment(sourceNode, "source");
            fragments.Add(sourceFragment);
            relations.Add(DecisionCpgFactory.CreateRelation(
              NLCPG.Contracts.NLCPGDecisionRelationKind.DerivedFrom,
              sourceFragment,
              anchorFragment));
            bindings.Add((sourceFragment, sourceNode));
        }

        var resolvedConflictKey = conflictKey ?? DecisionCpgFactory.BuildNodeKey(anchorNode);
        var unitNode = DecisionCpgFactory.CreateUnit(
          ruleId,
          DecisionActionKind.Delete,
          anchorFragment,
          reason: reason,
          conflictKey: resolvedConflictKey);
        relations.Insert(0, DecisionCpgFactory.CreateContainment(unitNode, anchorFragment));
        if (fragments.Count > 1)
        {
            relations.Insert(1, DecisionCpgFactory.CreateContainment(unitNode, fragments[1]));
        }

        var consumedKeys = (consumedNodes ?? new[] { anchorNode })
          .Select(DecisionCpgFactory.BuildNodeKey)
          .ToArray();
        var proofGoal = proof?.Goal.ToString() ??
          (declarationBoundary ? "DeclarationBoundary" : CoverageGoal.AtomicTarget.ToString());
        var resolvedComposition = composition == DecisionComposition.Unknown
          ? DecisionComposition.Independent
          : composition;
        var intent = EditIntent.Create(
          DecisionFootprint.Create(
            ruleId,
            anchorNode,
            DecisionActionKind.Delete,
            consumedKeys,
            resolvedComposition,
            proofGoal.ToString(),
            dominatesChildren,
            candidateDiscriminator: sourceNode is null
              ? null
              : DecisionCpgFactory.BuildNodeKey(sourceNode)).CandidateId,
          anchorNode,
          DecisionActionKind.Delete,
          consumedNodeKeys: consumedKeys,
          proofReferences: new[] { $"proof:{proofGoal}:{DecisionCpgFactory.BuildNodeKey(anchorNode)}" },
          behaviorBudget: proof is null
            ? null
            : new BehaviorBudget(proof.PreservedObligations
              .Select(obligation => Enum.TryParse<BehaviorObligationKind>(obligation.Kind, true, out var kind)
                ? (BehaviorObligationKind?)kind
                : null)
              .Where(kind => kind is not null)
              .Select(kind => kind!.Value)),
          composition: resolvedComposition,
          status: proof is null || proof.Status == CoverageProofStatus.Complete
            ? EditIntentStatus.Complete
            : EditIntentStatus.Unknown,
          dominatesChildren: dominatesChildren,
          declarationBoundary: declarationBoundary);
        var footprint = DecisionFootprint.Create(
          ruleId,
          anchorNode,
          DecisionActionKind.Delete,
          consumedKeys,
          resolvedComposition,
          proofGoal.ToString(),
          dominatesChildren,
          candidateDiscriminator: sourceNode is null
            ? null
            : DecisionCpgFactory.BuildNodeKey(sourceNode));

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Delete,
          unitNode,
          fragments,
          relations,
          DecisionCpgFactory.CreateSyntaxBindings(bindings.ToArray()),
          conflictKey: resolvedConflictKey,
          reason: reason,
          footprint: footprint,
          intent: intent);
    }

    private static NLCPG.Model.NLCPGNode CreateFragment(SyntaxNode node, string role, DecisionActionKind? localAction = null)
    {
        return DecisionCpgFactory.CreateFragment(
          $"frag:{DecisionCpgFactory.BuildNodeKey(node)}",
          node,
          role,
          localAction);
    }
}
