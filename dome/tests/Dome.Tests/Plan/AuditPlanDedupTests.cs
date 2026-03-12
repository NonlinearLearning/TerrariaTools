using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using Xunit;

namespace TerrariaTools.Dome.Tests.Plan;

public class AuditPlanDedupTests
{
    [Fact]
    public void Compile_DeduplicatesRepeatedSameActionForSingleTarget()
    {
        var target = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Update()"),
            MemberKind.Method,
            TargetKind.Statement,
            10,
            12,
            "Run();");

        var decisions = new[]
        {
            MarkDecision.ForTarget(target, PlanActionKind.Delete, "dome:delete", "seed"),
            MarkDecision.ForTarget(target, PlanActionKind.Delete, "dataflow-propagation", "propagated", sourceTargetKey: "seed-target")
        };

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            decisions);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Plan);
        Assert.Single(result.Plan!.Changes);
    }
}
