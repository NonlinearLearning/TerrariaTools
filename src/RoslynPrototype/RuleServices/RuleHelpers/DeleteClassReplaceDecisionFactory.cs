using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Model;
using Rules;

namespace RoslynPrototype.Decision;

public static class DeleteClassReplaceDecisionFactory
{
    public static DecisionUnit CreateLocalFunctionReplaceDecision(string ruleId, LocalFunctionStatementSyntax anchorNode, LocalFunctionStatementSyntax replacementNode, string reason)
    {
        return DeleteSObjectProposalHelpers.CreateStatementReplaceDecision(
          ruleId,
          anchorNode,
          replacementNode,
          reason);
    }

    public static DecisionUnit CreateMethodReplaceDecision(string ruleId, MethodDeclarationSyntax anchorNode, MethodDeclarationSyntax replacementNode, string reason)
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

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("replaced-with", anchorFragment, replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    public static DecisionUnit CreateInvocationReplaceDecision(string ruleId, InvocationExpressionSyntax anchorNode, InvocationExpressionSyntax replacementNode, string reason)
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

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("replaced-with", anchorFragment, replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    public static DecisionUnit CreateExpressionReplaceDecision(string ruleId, ExpressionSyntax anchorNode, ExpressionSyntax replacementNode, string reason)
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

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("replaced-with", anchorFragment, replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    public static DecisionUnit CreateIndexerReplaceDecision(string ruleId, IndexerDeclarationSyntax anchorNode, IndexerDeclarationSyntax replacementNode, string reason)
    {
        return CreateMemberDeclarationReplaceDecision(ruleId, anchorNode, replacementNode, reason);
    }

    public static DecisionUnit CreateDelegateReplaceDecision(string ruleId, DelegateDeclarationSyntax anchorNode, DelegateDeclarationSyntax replacementNode, string reason)
    {
        return CreateMemberDeclarationReplaceDecision(ruleId, anchorNode, replacementNode, reason);
    }

    public static DecisionUnit CreateElementAccessReplaceDecision(string ruleId, ElementAccessExpressionSyntax anchorNode, ElementAccessExpressionSyntax replacementNode, string reason)
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

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("replaced-with", anchorFragment, replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    private static DecisionUnit CreateMemberDeclarationReplaceDecision<TNode>(string ruleId, TNode anchorNode, TNode replacementNode, string reason)
      where TNode : MemberDeclarationSyntax
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

        return new DecisionUnit(
          ruleId,
          DecisionActionKind.Replace,
          unitNode,
          new[] { anchorFragment, replacementFragment },
          new[]
          {
            DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
            DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
            DecisionCpgFactory.CreateRelation("replaced-with", anchorFragment, replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
    }

    private static RoslynCpgNode CreateFragment(SyntaxNode node, string role, DecisionActionKind action)
    {
        return DecisionCpgFactory.CreateFragment(
          $"frag:{DecisionCpgFactory.BuildNodeKey(node)}",
          node,
          role,
          action);
    }
}
