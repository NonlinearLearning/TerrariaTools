using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Rules;
using NLISSN.Core.Lifting;

namespace NLISSN.Rules;

/// 为参数收缩生成带精确 CPG 片段锚点的替换决策，供冲突检测和改写阶段复用。
public static class ReplaceDecisionFactory
{
    // 为局部函数声明生成带锚点和替换片段的 Replace 决策。
    public static DecisionUnit CreateLocalFunctionReplaceDecision(string ruleId, LocalFunctionStatementSyntax anchorNode, LocalFunctionStatementSyntax replacementNode, string reason)
    {
        return ProposalHelpers.CreateStatementReplaceDecision(
          ruleId,
          anchorNode,
          replacementNode,
          reason);
    }

    // 为方法声明生成带冲突键和语法绑定的 Replace 决策。
    public static DecisionUnit CreateMethodReplaceDecision(
      string ruleId,
      MethodDeclarationSyntax anchorNode,
      MethodDeclarationSyntax replacementNode,
      string reason,
      NLCPGDecisionRelationKind relationKind = NLCPGDecisionRelationKind.ReplacedWith)
    {
        return CreateReplaceDecision(ruleId, anchorNode, replacementNode, reason, relationKind);
    }

    // 为调用表达式生成 Replace 决策，供参数收缩同步改写调用点。
    public static DecisionUnit CreateInvocationReplaceDecision(string ruleId, InvocationExpressionSyntax anchorNode, InvocationExpressionSyntax replacementNode, string reason)
    {
        return CreateReplaceDecision(
          ruleId,
          anchorNode,
          replacementNode,
          reason,
          NLCPGDecisionRelationKind.ReplacedWith);
    }

    // 为通用表达式锚点生成 Replace 决策，保留冲突检测和语法绑定信息。
    public static DecisionUnit CreateExpressionReplaceDecision(string ruleId, ExpressionSyntax anchorNode, ExpressionSyntax replacementNode, string reason)
    {
        return CreateReplaceDecision(
          ruleId,
          anchorNode,
          replacementNode,
          reason,
          NLCPGDecisionRelationKind.ReplacedWith);
    }

    // 为索引器声明复用成员声明替换决策构造逻辑。
    public static DecisionUnit CreateIndexerReplaceDecision(string ruleId, IndexerDeclarationSyntax anchorNode, IndexerDeclarationSyntax replacementNode, string reason)
    {
        return CreateReplaceDecision(
          ruleId,
          anchorNode,
          replacementNode,
          reason,
          NLCPGDecisionRelationKind.ReplacedWith);
    }

    // 为委托声明复用成员声明替换决策构造逻辑。
    public static DecisionUnit CreateDelegateReplaceDecision(string ruleId, DelegateDeclarationSyntax anchorNode, DelegateDeclarationSyntax replacementNode, string reason)
    {
        return CreateReplaceDecision(
          ruleId,
          anchorNode,
          replacementNode,
          reason,
          NLCPGDecisionRelationKind.ReplacedWith);
    }

    // 为元素访问生成 Replace 决策，供索引器参数收缩同步删除实参。
    public static DecisionUnit CreateElementAccessReplaceDecision(string ruleId, ElementAccessExpressionSyntax anchorNode, ElementAccessExpressionSyntax replacementNode, string reason)
    {
        return CreateReplaceDecision(
          ruleId,
          anchorNode,
          replacementNode,
          reason,
          NLCPGDecisionRelationKind.ReplacedWith);
    }

    private static DecisionUnit CreateReplaceDecision<TNode>(
      string ruleId,
      TNode anchorNode,
      TNode replacementNode,
      string reason,
      NLCPGDecisionRelationKind relationKind)
      where TNode : SyntaxNode
    {
        var anchorFragment = CreateFragment(anchorNode, "anchor", DecisionActionKind.Replace);
        var replacementFragment = CreateFragment(
          replacementNode.WithoutTrivia(),
          "replacement",
          DecisionActionKind.Replace);
        var unitNode = DecisionCpgFactory.CreateUnit(
          ruleId,
          DecisionActionKind.Replace,
          anchorFragment,
          reason: reason,
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode));

        var anchorKey = DecisionCpgFactory.BuildNodeKey(anchorNode);
        var footprint = DecisionFootprint.Create(
          ruleId,
          anchorNode,
          DecisionActionKind.Replace,
          new[] { anchorKey },
          DecisionComposition.Composable,
          proofKind: CoverageGoal.ExpressionReplacement.ToString(),
          candidateDiscriminator: DecisionCpgFactory.BuildNodeKey(replacementNode));
        var intent = EditIntent.Create(
          footprint.CandidateId,
          anchorNode,
          DecisionActionKind.Replace,
          consumedNodeKeys: new[] { anchorKey },
          writeNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(replacementNode) },
          proofReferences: new[] { $"proof:{CoverageGoal.ExpressionReplacement}:{anchorKey}" },
          composition: DecisionComposition.Composable,
          status: EditIntentStatus.Complete);

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation(
              relationKind,
              anchorFragment,
              replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason,
          footprint: footprint,
          intent: intent);
    }

    private static NLCPGNode CreateFragment(SyntaxNode node, string role, DecisionActionKind action)
    {
        return DecisionCpgFactory.CreateFragment(
          $"frag:{DecisionCpgFactory.BuildNodeKey(node)}",
          node,
          role,
          action);
    }
}
