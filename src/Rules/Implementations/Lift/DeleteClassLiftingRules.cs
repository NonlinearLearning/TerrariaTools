using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassExpressionHostLiftingRule : RuleDefinitionLift
{
    public override string RuleId { get; } = DeleteClassRuleIds.HostLiftRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Lift delete-class marks to direct expression and statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    public override IEnumerable<LiftedMarkRecord> Lift(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return DeleteSObjectHostLiftingHelpers.BuildHostLiftedMarks(
          context,
          RuleId,
          seedMarks,
          propagatedMarks);
    }
}

public sealed class DeleteClassIfStructureLiftingRule : RuleDefinitionLift
{
    public override string RuleId { get; } = DeleteClassRuleIds.IfStructureLiftRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Lift delete-class marks into if/elseif/else structure tails";

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

public sealed class DeleteClassSwitchStructureLiftingRule : RuleDefinitionLift
{
    public override string RuleId { get; } = DeleteClassRuleIds.SwitchStructureLiftRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Lift delete-class marks through switch structures";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      DeleteSObjectLiftingCommon.AllowedLiftNodeKinds;

    public override IEnumerable<LiftedMarkRecord> Lift(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
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
