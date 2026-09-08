using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 将删除类的 TypeSyntax seed mark 映射到拥有该类型语法的可改写声明。
public static class TypeSyntaxProposalHelpers
{
    private const string TypeSyntaxMarkRuleId = "mark.type.type-syntax";

    // 把 delete-class 的 TypeSyntax seed mark 映射到唯一声明宿主，并直接产出删除决策。
    public static IEnumerable<DecisionUnit> CreateDeleteDecisions<TNode>(string ruleId, string reason, IReadOnlyList<MarkRecord> seedMarks, Func<TypeSyntax, TNode?> resolver)
      where TNode : SyntaxNode
    {
        var handledNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seedMark in seedMarks)
        {
            if (!string.Equals(seedMark.RuleId, TypeSyntaxMarkRuleId, StringComparison.Ordinal) ||
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
