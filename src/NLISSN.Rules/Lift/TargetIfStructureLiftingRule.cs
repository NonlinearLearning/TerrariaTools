using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class SObjectIfStructureLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.target.if-structure";

    public override string RuleId { get; } = "DEL-SOBJ-LIFT-IF-001";

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Lift s-object marks into if/elseif/else structure tails";

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
