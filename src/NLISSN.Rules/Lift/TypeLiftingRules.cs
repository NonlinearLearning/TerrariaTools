using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class ClassExpressionHostLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.type.expression-host";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-HOST-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Lift delete-class marks to direct expression and statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return DeleteSObjectHostLiftingHelpers.BuildHostLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks);
    }
}

public sealed class ClassIfStructureLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.type.if-structure";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-IF-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Lift delete-class marks into if/elseif/else structure tails";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return DeleteSObjectIfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks);
    }
}

public sealed class ClassSwitchStructureLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.type.switch-structure";

    public override string RuleId { get; } = "DEL-CLASS-LIFT-SWITCH-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Lift delete-class marks through switch structures";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

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
}
