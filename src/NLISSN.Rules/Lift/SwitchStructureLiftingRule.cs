using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将 switch 中可完整规约的分支事实提升为单一结构宿主。
public sealed class SwitchStructureLiftingRule : RuleDefinitionLift
{
    private static readonly RuleFactKind IfStructureFactKind = RuleFactKind.LiftIfStructure;

    private static readonly RuleFactKind ExpressionHostFactKind = RuleFactKind.LiftExpressionHost;

    private static readonly RuleFactKind SwitchStructureFactKind = RuleFactKind.LiftSwitchStructure;

    private static readonly RuleConsumesContract SwitchConsumes = new(
      new[]
      {
        new RuleConsumedSyntax(
          LiftingCommon.AllowedLiftNodeKinds,
          ExpressionHostFactKind),
        new RuleConsumedSyntax(
          new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause },
          IfStructureFactKind)
      });


    public override string RuleId { get; } = "lift.target.switch-structure";

    public override RuleConsumesContract Consumes => SwitchConsumes;

    public override RuleProducesContract Produces => new(new[]
    {
      new RuleProducedSyntax(
        new[] { SyntaxKind.SwitchSection, SyntaxKind.SwitchStatement },
        SwitchStructureFactKind)
    });


    public override string Name { get; } = "Lift s-object marks into switch section and switch statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      LiftingCommon.AllowedLiftNodeKinds;

    // 先做宿主与 if 提升，再判断是否可以把整段 switch 规约成结构级标记。
    public override IEnumerable<LiftedMarkRecord> Lift(ILiftRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var hostLiftedMarks = ExpressionHostLiftingHelpers.BuildHostLiftedMarks(
            context,
            RuleId,
            seedMarks,
            propagatedMarks)
          .ToList();
        var ifLiftedMarks = IfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
            context,
            RuleId,
            seedMarks,
            propagatedMarks)
          .ToList();

        return SwitchStructureLiftingHelpers.BuildSwitchLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks,
          hostLiftedMarks.Concat(ifLiftedMarks).ToList());
    }

    public override IEnumerable<LiftedMarkRecord> Lift(ILiftRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        return SwitchStructureLiftingHelpers.BuildSwitchLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks,
          existingLiftedMarks
            .Where(mark => mark.Mark.OutputKind is RuleOutputKind.ExpressionHost or RuleOutputKind.IfStructure)
            .ToList());
    }
}
