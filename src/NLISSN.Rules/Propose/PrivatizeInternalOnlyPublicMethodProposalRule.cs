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
    private static readonly RuleConsumesContract InternalOnlyPublicMethodConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.MethodDeclaration },
          InternalOnlyPublicMethodFacts.Lifted)
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
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = propagatedMarks;

        foreach (var liftedMark in liftedMarks)
        {
            if (liftedMark.Mark.SemanticTag != InternalOnlyPublicMethodFacts.Lifted ||
                liftedMark.Mark.SyntaxNode is not MethodDeclarationSyntax method ||
                !TryBuildPrivateMethod(method, out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              method,
              replacementMethod,
              "Public method has no external references; change accessibility to private.",
              NLCPGDecisionRelationKind.AccessibilityToPrivate);
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

}
