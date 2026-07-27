using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 仅为已证明无引用的方法生成声明删除决策。
public sealed class UnreferencedMethodProposalRule : RuleDefinitionPropose
{
  public override string CapabilityId { get; } = "propose.unreferenced-method";

    public override string RuleId { get; } = "DEL-UNREF-METHOD-PROP-001";

  public override string GroupKey { get; } = "DEL-UNREF-METHOD";

  public override string Name { get; } = "Delete unreferenced private method declarations";

  public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
    Array.Empty<SyntaxKind>();

  // 为已证明无剩余引用的私有方法声明直接生成删除决策。
  public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
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
