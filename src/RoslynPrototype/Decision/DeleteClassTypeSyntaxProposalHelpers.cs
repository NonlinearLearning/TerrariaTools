using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;
using Rules;

namespace RoslynPrototype.Decision;

internal static class DeleteClassTypeSyntaxProposalHelpers
{
    internal static IEnumerable<DecisionUnit> CreateDeleteDecisions<TNode>(string ruleId, string reason, IReadOnlyList<MarkRecord> seedMarks, Func<TypeSyntax, TNode?> resolver)
      where TNode : SyntaxNode
    {
        var handledNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!string.Equals(seedMark.RuleId, DeleteClassRuleIds.TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
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

            yield return RuleAnalysisHelpers.CreateDeleteDecision(
              ruleId,
              resolvedNode,
              reason,
              typeSyntax,
              nodeKey);
        }
    }
}
