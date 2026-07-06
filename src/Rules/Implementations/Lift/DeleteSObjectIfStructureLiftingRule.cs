using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteSObjectIfStructureLiftingRule : RuleDefinitionLift
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.IfStructureLiftRuleId;

    public override string GroupKey { get; } = DeleteSObjectRuleIds.GroupKey;

    public override string Name { get; } = "Lift s-object marks into if/elseif/else structure tails";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    public override IEnumerable<LiftedMarkRecord> Lift(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return DeleteSObjectIfStructureLiftingHelpers.BuildIfStructureLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks);
    }
}
