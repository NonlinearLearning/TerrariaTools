using TerrariaTools.Dome.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

// Legacy internal coverage for the boundary promotion runtime behind the scenes.
public sealed class BoundaryPromotionEngineLegacyUnitTests
{
    [Fact]
    public async Task Promote_UsesRoslynModelContext()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update(int value)
                {
                    // dome:delete
                    fun2(value);
                }

                private void fun2(int i)
                {
                }
            }
            """);

        var context = analysis.CreateContext();
        var statementDecision = CreateDeleteDecision(context.View.Targets.Single(target => target.Locator.DisplayText == "fun2(value);"));
        var decisions = new[] { statementDecision };

        var promoted = new BoundaryPromotionEngine(MarkingRuleRegistry.CreateDefault()).Promote(
            context,
            decisions,
            context.View.Targets.ToDictionary(target => $"{target.Target.IdentityKey}|{target.Locator.EffectiveResolutionKey.SpanStart}|{target.Locator.EffectiveResolutionKey.SpanLength}", StringComparer.Ordinal));

        var promotedDecision = Assert.Single(promoted);
        Assert.Equal("boundary-promotion", promotedDecision.Reason.RuleId);
        Assert.Equal(ModelPrimitives.TargetKind.Method, promotedDecision.Target.TargetKind);
        Assert.Equal("Sample.Player.fun2(int)", promotedDecision.Target.MemberId.Value);
    }

    [Fact]
    public async Task Promote_DoesNotPromotePropagatedDelete()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update(int value)
                {
                    int count = value;
                    fun2(count);
                }

                private void fun2(int i)
                {
                }
            }
            """);

        var context = analysis.CreateContext();
        var invocationTarget = context.View.Targets.Single(target => target.Locator.DisplayText == "fun2(count);");
        var propagatedDelete = CreatePropagatedDecision(invocationTarget);

        var promoted = new BoundaryPromotionEngine(MarkingRuleRegistry.CreateDefault()).Promote(
            context,
            new[] { propagatedDelete },
            context.View.Targets.ToDictionary(target => $"{target.Target.IdentityKey}|{target.Locator.EffectiveResolutionKey.SpanStart}|{target.Locator.EffectiveResolutionKey.SpanLength}", StringComparer.Ordinal));

        Assert.Empty(promoted);
    }

    private static ModelRules.MarkDecision CreateDeleteDecision(ModelAnalysis.AnalysisTarget target) =>
        new(
            target.Target,
            target.Locator,
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete, null),
            new ModelRules.PlanReason("boundary-promotion", "Legacy propagation test"));

    private static ModelRules.MarkDecision CreatePropagatedDecision(ModelAnalysis.AnalysisTarget target) =>
        new(
            target.Target,
            target.Locator,
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete, null),
            new ModelRules.PlanReason("dataflow-propagation", "propagated", Origin: ModelPrimitives.DecisionOrigin.Propagation));

    private static async Task<ApplicationAbstractions.AnalysisEngineResult> AnalyzeAsync(string sourceText) =>
        await ((ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine()).AnalyzeAsync(
            new ApplicationAbstractions.SourceDocumentSet(
                "Sample.cs",
                "Sample.cs",
                [
                    new ApplicationAbstractions.SourceDocument("Sample.cs", "Sample.cs", sourceText)
                ]),
            CancellationToken.None);
}
