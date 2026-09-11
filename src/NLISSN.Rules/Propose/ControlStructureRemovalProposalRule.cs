using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;

namespace NLISSN.Rules;

/// 将传播后的完整控制结构标记转换为删除决策；只接收声明的冲突节点种类。
[global::NLISSN.Core.Pipeline.RuleRegistration]
public sealed class ControlStructureRemovalProposalRule : RuleDefinitionPropose
{
    private static readonly RuleConsumesContract StructuralFactsConsumes =
      StructuralControlProposalContracts.CreateConsumes();


    public override string RuleId { get; } = "propose.control-structure-removal";

    public override RuleConsumesContract Consumes => StructuralFactsConsumes;


    public override string Name { get; } = "Match s-rooted control structure delete decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      ProposalHelpers.ControlConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    public override RuleTransformationContract TransformationContract { get; } = new(
      "rewrite.control-structure",
      new HashSet<DecisionActionKind> { DecisionActionKind.Delete },
      new HashSet<RewriteControlFlowEffect>
      {
          RewriteControlFlowEffect.RemoveLoop,
          RewriteControlFlowEffect.RemoveSwitchSection
      },
      true,
      false);

    // 把已收束到控制结构宿主的派生 mark 转成直接删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;

        foreach (var liftedMark in liftedMarks.Where(mark =>
                   mark.StructureKind is StructuralKind.Loop or StructuralKind.Switch or StructuralKind.Return &&
                   HasCompleteStructureProof(mark)))
        {
            var kind = (SyntaxKind)liftedMark.Mark.SyntaxNode.RawKind;
            if (!DecisionConflictNodeKinds.Contains(kind))
            {
                continue;
            }

            var decision = DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              liftedMark.Mark.SyntaxNode,
              liftedMark.Mark.Reason,
              liftedMark.SourceMark.SyntaxNode,
              proof: liftedMark.Payload switch
              {
                ControlStructureLiftPayload control => control.Proof,
                SwitchStructureLiftPayload @switch => @switch.Proof,
                _ => null
              },
              composition: DecisionComposition.OpaqueDominates,
              dominatesChildren: true);
            var effect = GetEffect(kind);
            yield return effect is null
              ? decision
              : decision with
              {
                  TransformationContract = TransformationContract,
                  ControlFlowEffects = new[] { effect.Value },
                  PreservedSpans = GetFollowingStatementSpans(liftedMark.Mark.SyntaxNode)
              };
        }
    }

    private static bool HasCompleteStructureProof(LiftedMarkRecord liftedMark)
    {
        return liftedMark.Payload switch
        {
            ControlStructureLiftPayload control =>
              control.Proof.Goal == CoverageGoal.StructureComplete && control.Proof.IsComplete,
            SwitchStructureLiftPayload @switch =>
              @switch.Proof.Goal == CoverageGoal.StructureComplete && @switch.Proof.IsComplete,
            _ => false
        };
    }

    private static RewriteControlFlowEffect? GetEffect(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.ForStatement or
            SyntaxKind.ForEachStatement or
            SyntaxKind.ForEachVariableStatement or
            SyntaxKind.WhileStatement or
            SyntaxKind.DoStatement => RewriteControlFlowEffect.RemoveLoop,
            SyntaxKind.SwitchStatement or SyntaxKind.SwitchSection => RewriteControlFlowEffect.RemoveSwitchSection,
            _ => null
        };
    }

    private static IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> GetFollowingStatementSpans(SyntaxNode node)
    {
        if (node.Parent is not BlockSyntax block || node is not StatementSyntax statement)
        {
            return Array.Empty<Microsoft.CodeAnalysis.Text.TextSpan>();
        }

        var index = block.Statements.IndexOf(statement);
        return index >= 0 && index + 1 < block.Statements.Count
          ? new[] { block.Statements[index + 1].Span }
          : Array.Empty<Microsoft.CodeAnalysis.Text.TextSpan>();
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
          RuleFactKind.LiftControlStructure),
        new RuleConsumedSyntax(
          new[] { SyntaxKind.SwitchSection, SyntaxKind.SwitchStatement },
          RuleFactKind.LiftSwitchStructure)
      });
    }
}
