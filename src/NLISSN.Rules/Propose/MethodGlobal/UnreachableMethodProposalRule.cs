using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将不可达方法的阶段事实转换为方法声明删除决策。
public sealed class UnreachableMethodProposalRule : RuleDefinitionPropose
{
    private static readonly RuleFactKind UnreachableMethodFactKind = RuleFactKind.UnreachableMethod;

    private static readonly RuleConsumesContract UnreachableMethodConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnreachableMethodFactKind)
      });


    public override string RuleId { get; } = "propose.unreachable-method";

    public override RuleConsumesContract Consumes => UnreachableMethodConsumes;

    public override string Name { get; } = "Match unreachable methods by graph reachability proposal";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
      Array.Empty<SyntaxKind>();

    // 为标记阶段已经证明不可达的方法直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = propagatedMarks;
        _ = liftedMarks;

        foreach (var seedMark in seedMarks)
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              seedMark.SyntaxNode,
              seedMark.Reason);
        }
    }
}
