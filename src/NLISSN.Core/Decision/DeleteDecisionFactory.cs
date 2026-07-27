using Microsoft.CodeAnalysis;
using NLISSN.Core.Marking;

namespace NLISSN.Core.Decision;

public static class DeleteDecisionFactory
{
    // 为一个锚点语法节点创建删除决策单元，并在需要时附带来源片段关系。
    public static DecisionUnit CreateDeleteDecision(string ruleId, SyntaxNode anchorNode, string reason, SyntaxNode? sourceNode = null, string? conflictKey = null)
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

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Delete,
          unitNode,
          fragments,
          relations,
          DecisionCpgFactory.CreateSyntaxBindings(bindings.ToArray()),
          conflictKey: resolvedConflictKey,
          reason: reason);
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
