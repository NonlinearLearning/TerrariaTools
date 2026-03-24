using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;
using Xunit;

namespace TerrariaTools.Dome.Tests.Plan;

public class AuditPlanDedupTests
{
    [Fact]
    public void Compile_DeduplicatesRepeatedSameActionForSingleTarget()
    {
        var targetIdentity = new ModelPrimitives.TargetIdentity(
            "Sample.cs",
            new ModelPrimitives.MemberId("Sample.Player.Update()"),
            ModelPrimitives.MemberKind.Method,
            ModelPrimitives.TargetKind.Statement);
        var targetLocator = new ModelPrimitives.TargetLocator(10, 12, "Run();");

        var decisions = new[]
        {
            new ModelRules.MarkDecision(
                targetIdentity,
                targetLocator,
                new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                new ModelRules.PlanReason("dome:delete", "seed")),
            new ModelRules.MarkDecision(
                targetIdentity,
                targetLocator,
                new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                new ModelRules.PlanReason("dataflow-propagation", "propagated", SourceTargetKey: "seed-target"))
        };

        var result = ModelPlanning.AuditPlanCompiler.Compile(
            new ModelPlanning.PlanMetadata("dome", "1", "input.cs", "out", ModelPrimitives.RunMode.Standard),
            decisions);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Plan);
        var change = Assert.Single(result.Plan!.Changes);
        Assert.Equal("Sample.Player.Update()", change.Target.MemberId.Value);
        Assert.Equal("Run();", change.Locator.DisplayText);
        Assert.Equal(10, change.Locator.SpanStart);
        Assert.IsType<ModelPlanning.PlanReason>(change.Reason);
    }
}
