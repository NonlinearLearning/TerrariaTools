using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将 if 结构的完成态传播事实转换为结构化 Lift 记录。
public sealed class SObjectIfStructureLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.target.if-structure";

    public override string RuleId { get; } = "DEL-SOBJ-LIFT-IF-001";

    public override IReadOnlyList<RuleOutputKind> ProducedOutputs =>
      new[] { RuleOutputKind.LiftedMark, RuleOutputKind.IfStructure };

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Lift s-object marks into if/elseif/else structure tails";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    // 在 if / else if / else 已具备完整删除条件时，产出结构级 lifted mark。
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
