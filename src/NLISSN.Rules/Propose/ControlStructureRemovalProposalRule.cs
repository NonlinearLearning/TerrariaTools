using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将传播后的完整控制结构标记转换为删除决策；只接收声明的冲突节点种类。
public sealed class ControlStructureRemovalProposalRule : RuleDefinitionPropose
{
    private static readonly RuleConsumesContract StructuralFactsConsumes =
      StructuralControlProposalContracts.CreateConsumes();

    public override string CapabilityId { get; } = "propose.control-structure-removal";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-CTRL-001";

    public override RuleConsumesContract Consumes => StructuralFactsConsumes;


    public override string Name { get; } = "Match s-rooted control structure delete decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      ProposalHelpers.ControlConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 把已收束到控制结构宿主的派生 mark 转成直接删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;

        foreach (var liftedMark in liftedMarks.Where(mark =>
                   mark.StructureKind is StructuralKind.Loop or StructuralKind.Switch or StructuralKind.Return))
        {
            var kind = (SyntaxKind)liftedMark.Mark.SyntaxNode.RawKind;
            if (!DecisionConflictNodeKinds.Contains(kind))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              liftedMark.Mark.SyntaxNode,
              liftedMark.Mark.Reason,
              liftedMark.SourceMark.SyntaxNode);
        }
    }
}

internal static class StructuralControlProposalContracts
{
    public static RuleConsumesContract CreateConsumes()
    {
        return new RuleConsumesContract(new[]
        {
        new RuleConsumedSyntax(
          new[]
          {
            SyntaxKind.ForStatement, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement,
            SyntaxKind.WhileStatement, SyntaxKind.DoStatement, SyntaxKind.ReturnStatement
          },
          RuleFactPorts.LiftControlStructure),
        new RuleConsumedSyntax(
          new[] { SyntaxKind.SwitchSection, SyntaxKind.SwitchStatement },
          RuleFactPorts.LiftSwitchStructure)
      });
    }
}
