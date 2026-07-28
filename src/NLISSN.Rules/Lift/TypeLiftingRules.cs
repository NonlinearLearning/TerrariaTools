using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 删除类链路的表达式宿主提升规则。
public sealed class ClassExpressionHostLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.type.expression-host";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-HOST-001";

    public override IReadOnlyList<RuleOutputKind> ProducedOutputs =>
      new[] { RuleOutputKind.LiftedMark, RuleOutputKind.ExpressionHost };

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Lift delete-class marks to direct expression and statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 复用通用宿主提升逻辑，把删除类命中提升到最小可改写的表达式或语句宿主。
    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return DeleteSObjectHostLiftingHelpers.BuildHostLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks)
          .Select(mark => mark with { Mark = mark.Mark with { OutputKind = RuleOutputKind.ExpressionHost } });
    }
}

/// 删除类链路的 if 结构完成态提升规则。
public sealed class ClassIfStructureLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.type.if-structure";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-IF-001";

    public override IReadOnlyList<RuleOutputKind> ProducedOutputs =>
      new[] { RuleOutputKind.LiftedMark, RuleOutputKind.IfStructure };

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Lift delete-class marks into if/elseif/else structure tails";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 仅在 if 结构已形成完整删除条件时，补出后续提案需要的 lifted mark。
    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return DeleteSObjectIfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks)
          .Select(mark => mark with { Mark = mark.Mark with { OutputKind = RuleOutputKind.IfStructure } });
    }
}

/// 删除类链路的 switch 结构完成态提升规则。
public sealed class ClassSwitchStructureLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.type.switch-structure";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-SWITCH-001";

    public override IReadOnlyList<RuleDependency> Dependencies =>
      RuleGraphDependencyCatalog.GetDependencies(
        this,
        RuleKind.Lift,
        new[]
        {
          new RuleDependency(RuleNodeId.For(RuleKind.Lift, "DEL-CLASS-LIFT-HOST-001"), RuleOutputKind.ExpressionHost),
          new RuleDependency(RuleNodeId.For(RuleKind.Lift, "DEL-CLASS-LIFT-IF-001"), RuleOutputKind.IfStructure)
        });

    public override IReadOnlyList<RuleOutputKind> ProducedOutputs =>
      new[] { RuleOutputKind.LiftedMark, RuleOutputKind.SwitchStructure };

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Lift delete-class marks through switch structures";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 先收集宿主与 if 提升结果，再把它们继续折叠成可整体规约的 switch 结构标记。
    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var hostLiftedMarks = DeleteSObjectHostLiftingHelpers.BuildHostLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks).ToList();
        var ifLiftedMarks = DeleteSObjectIfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks).ToList();

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
