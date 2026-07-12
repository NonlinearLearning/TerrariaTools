using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;
using Rules;

namespace RoslynPrototype.Decision;

public static class DeleteClassTypeSyntaxProposalHelpers
{
    private const string DeleteClassTypeSyntaxMarkRuleId = "DEL-CLASS-MARK-TYPE-001";

    public static IEnumerable<DecisionUnit> CreateDeleteDecisions<TNode>(string ruleId, string reason, IReadOnlyList<MarkRecord> seedMarks, Func<TypeSyntax, TNode?> resolver)
      where TNode : SyntaxNode
    {
        var handledNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!string.Equals(seedMark.RuleId, DeleteClassTypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
                seedMark.SyntaxNode is not TypeSyntax typeSyntax)
            {
                continue;
            }

            var resolvedNode = resolver(typeSyntax);
            if (resolvedNode is null)
            {
                continue;
            }

            var nodeKey = DecisionCpgFactory.BuildNodeKey(resolvedNode);
            if (!handledNodes.Add(nodeKey))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              ruleId,
              resolvedNode,
              reason,
              typeSyntax,
              nodeKey);
        }
    }
}
