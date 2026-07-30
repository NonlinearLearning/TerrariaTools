using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将 switch 中可完整规约的分支事实提升为单一结构宿主。
public sealed class SObjectSwitchStructureLiftingRule : RuleDefinitionLift
{
    private static readonly RuleSemanticTag IfStructureSemanticTag = new("SObject.IfStructure");

    private static readonly RuleSemanticTag ExpressionHostSemanticTag = new("SObject.ExpressionHost");

    private static readonly RuleConsumesContract SwitchConsumes = new(
      new[]
      {
        new RuleConsumedSyntax(
          DeleteSObjectLiftingCommon.AllowedLiftNodeKinds,
          ExpressionHostSemanticTag),
        new RuleConsumedSyntax(
          new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause },
          IfStructureSemanticTag)
      });

    public override string CapabilityId { get; } = "lift.target.switch-structure";

    public override string RuleId { get; } = "DEL-SOBJ-LIFT-SWITCH-001";

    public override RuleConsumesContract Consumes => SwitchConsumes;


    public override string Name { get; } = "Lift s-object marks into switch section and switch statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 先做宿主与 if 提升，再判断是否可以把整段 switch 规约成结构级标记。
    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var hostLiftedMarks = DeleteSObjectHostLiftingHelpers.BuildHostLiftedMarks(
            context,
            RuleId,
            seedMarks,
            propagatedMarks)
          .ToList();
        var ifLiftedMarks = DeleteSObjectIfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
            context,
            RuleId,
            seedMarks,
            propagatedMarks)
          .ToList();

        return DeleteSObjectSwitchLiftingHelpers.BuildSwitchLiftedMarks(
          RuleId,
          seedMarks,
          propagatedMarks,
          hostLiftedMarks.Concat(ifLiftedMarks).ToList());
    }

    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        _ = context;
        return DeleteSObjectSwitchLiftingHelpers.BuildSwitchLiftedMarks(
          RuleId,
          seedMarks,
          propagatedMarks,
          existingLiftedMarks
            .Where(mark => mark.Mark.OutputKind is RuleOutputKind.ExpressionHost or RuleOutputKind.IfStructure)
            .ToList());
    }
}
