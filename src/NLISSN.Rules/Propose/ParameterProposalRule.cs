using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class ParameterProposalRule : RuleDefinitionPropose
{

    public override string RuleId { get; } = "propose.type.parameter";


    public override string Name { get; } = "Delete private methods whose parameter type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 当前刻意不为“整方法参数删除”直接产出决策，避免与更细粒度的收缩规则冲突。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = propagatedMarks;
        _ = liftedMarks;
        _ = seedMarks;
        yield break;
    }
}

