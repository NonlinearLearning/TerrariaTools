using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 仅在所有调用点都位于当前程序集内部时，将公开方法的可见性改为 private。
public sealed class PrivatizeInternalOnlyPublicMethodProposalRule : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag InternalOnlyPublicMethodSemanticTag = new("InternalOnlyPublicMethod");

    private static readonly RuleConsumesContract InternalOnlyPublicMethodConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.MethodDeclaration },
          InternalOnlyPublicMethodSemanticTag)
      });

    public override string CapabilityId { get; } = "propose.privatize-internal-only-public-method";

    public override string RuleId { get; } = "PRIV-INTERNAL-PUBLIC-PROP-001";

    public override RuleConsumesContract Consumes => InternalOnlyPublicMethodConsumes;


    public override string Name { get; } = "Replace public method modifiers with private";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
      Array.Empty<SyntaxKind>();

    // 把仅内部使用的 public 方法改写为 private，并保留原有签名主体不变。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
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

    private static bool TryBuildPrivateMethod(MethodDeclarationSyntax method, out MethodDeclarationSyntax replacementMethod)
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

    private static DecisionUnit CreateMethodReplaceDecision(string ruleId, MethodDeclarationSyntax anchorNode, MethodDeclarationSyntax replacementNode, string reason)
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
        DecisionCpgFactory.CreateRelation(
          NLCPGDecisionRelationKind.AccessibilityToPrivate,
          anchorFragment,
          replacementFragment)
          },
          DecisionCpgFactory.CreateSyntaxBindings(
            (anchorFragment, anchorNode),
            (replacementFragment, replacementNode.WithoutTrivia())),
          conflictKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          mergeKey: DecisionCpgFactory.BuildNodeKey(anchorNode),
          reason: reason);
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
