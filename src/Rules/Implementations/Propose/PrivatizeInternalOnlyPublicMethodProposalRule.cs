using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class PrivatizeInternalOnlyPublicMethodProposalRule : RuleDefinitionPropose
{
  public override string RuleId { get; } = PrivatizeInternalOnlyPublicMethodRuleIds.ProposalRuleId;

  public override string GroupKey { get; } = PrivatizeInternalOnlyPublicMethodRuleIds.GroupKey;

  public override string Name { get; } = "Replace public method modifiers with private";

  public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
    Array.Empty<SyntaxKind>();

  public override IEnumerable<DecisionUnit> Propose(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
    IReadOnlyList<LiftedMarkRecord> liftedMarks)
  {
    _ = context;
    _ = propagatedMarks;
    _ = liftedMarks;

    foreach (var seedMark in seedMarks)
    {
      if (seedMark.SyntaxNode is not MethodDeclarationSyntax method ||
          !TryBuildPrivateMethod(method, out var replacementMethod))
      {
        continue;
      }

      yield return CreateMethodReplaceDecision(
        RuleId,
        method,
        replacementMethod,
        "Public method has no external references; change accessibility to private.");
    }
  }

  private static bool TryBuildPrivateMethod(
    MethodDeclarationSyntax method,
    out MethodDeclarationSyntax replacementMethod)
  {
    replacementMethod = method;
    var publicToken = method.Modifiers.FirstOrDefault(token => token.IsKind(SyntaxKind.PublicKeyword));
    if (publicToken.RawKind == 0)
    {
      return false;
    }

    var privateToken = SyntaxFactory.Token(SyntaxKind.PrivateKeyword)
      .WithTriviaFrom(publicToken);
    replacementMethod = method.ReplaceToken(publicToken, privateToken);
    return true;
  }

  private static DecisionUnit CreateMethodReplaceDecision(
    string ruleId,
    MethodDeclarationSyntax anchorNode,
    MethodDeclarationSyntax replacementNode,
    string reason)
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
        DecisionCpgFactory.CreateRelation("accessibility-to-private", anchorFragment, replacementFragment)
      },
      DecisionCpgFactory.CreateSyntaxBindings(
        (anchorFragment, anchorNode),
        (replacementFragment, replacementNode.WithoutTrivia())),
      conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
      mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
      reason: reason);
  }

  private static RoslynCpgNode CreateFragment(
    SyntaxNode node,
    string role,
    DecisionActionKind action)
  {
    return DecisionCpgFactory.CreateFragment(
      $"frag:{DecisionCpgFactory.BuildNodeKey(node)}",
      node,
      role,
      action);
  }
}
