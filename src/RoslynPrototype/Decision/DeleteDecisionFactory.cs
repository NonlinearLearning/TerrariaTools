using Microsoft.CodeAnalysis;
using RoslynPrototype.Marking;

namespace RoslynPrototype.Decision;

public static class DeleteDecisionFactory
{
    public static DecisionUnit CreateDeleteDecision(string ruleId, SyntaxNode anchorNode, string reason, SyntaxNode? sourceNode = null, string? conflictKey = null)
    {
        var anchorFragment = CreateFragment(anchorNode, "anchor", DecisionActionKind.Delete);
        var fragments = new List<MinimalRoslynCpg.Model.RoslynCpgNode> { anchorFragment };
        var relations = new List<MinimalRoslynCpg.Model.RoslynCpgEdge>();
        var bindings = new List<(MinimalRoslynCpg.Model.RoslynCpgNode Fragment, SyntaxNode Node)>
        {
          (anchorFragment, anchorNode)
        };

        if (sourceNode is not null && !ReferenceEquals(sourceNode, anchorNode))
        {
            var sourceFragment = CreateFragment(sourceNode, "source");
            fragments.Add(sourceFragment);
            relations.Add(DecisionCpgFactory.CreateRelation("derived-from", sourceFragment, anchorFragment));
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

    private static MinimalRoslynCpg.Model.RoslynCpgNode CreateFragment(SyntaxNode node, string role, DecisionActionKind? localAction = null)
    {
        return DecisionCpgFactory.CreateFragment(
          $"frag:{DecisionCpgFactory.BuildNodeKey(node)}",
          node,
          role,
          localAction);
    }
}
