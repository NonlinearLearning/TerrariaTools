using TerrariaTools.Dome.Model.Planning;
using TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Model.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Plan;

public class AuditPlanDedupTests
{
    [Fact]
    public void Compile_DeduplicatesRepeatedSameActionForSingleTarget()
    {
        var targetIdentity = new TargetIdentity(
            "Sample.cs",
            new MemberId("Sample.Player.Update()"),
            MemberKind.Method,
            TargetKind.Statement);
        var targetLocator = new TargetLocator(10, 12, "Run();");

        var decisions = new[]
        {
            new MarkDecision(
                targetIdentity,
                targetLocator,
                new PlanAction(PlanActionKind.Delete),
                new PlanReason("dome:delete", "seed")),
            new MarkDecision(
                targetIdentity,
                targetLocator,
                new PlanAction(PlanActionKind.Delete),
                new PlanReason("dataflow-propagation", "propagated", SourceTargetKey: "seed-target"))
        };

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            decisions);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Plan);
        var change = Assert.Single(result.Plan!.Changes);
        Assert.Equal("Sample.Player.Update()", change.Target.MemberId.Value);
        Assert.Equal("Run();", change.Locator.DisplayText);
        Assert.Equal(10, change.Locator.SpanStart);
        Assert.IsType<PlanReason>(change.Reason);
    }
}
