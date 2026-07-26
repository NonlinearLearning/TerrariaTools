using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class SObjectExpressionHostLiftingRule : RuleDefinitionLift
{
    public override string CapabilityId { get; } = "lift.target.expression-host";

    public override string RuleId { get; } = "DEL-SOBJ-LIFT-HOST-001";

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Lift s-object marks to direct expression and statement hosts";

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
