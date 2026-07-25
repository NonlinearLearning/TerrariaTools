using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class SObjectSwitchStructureLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.target.switch-structure";

    public override string RuleId { get; } = "DEL-SOBJ-LIFT-SWITCH-001";

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Lift s-object marks into switch section and switch statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

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
}
