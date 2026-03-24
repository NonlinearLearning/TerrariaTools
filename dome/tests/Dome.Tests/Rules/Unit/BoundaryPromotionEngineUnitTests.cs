using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;
using TerrariaTools.Dome.Core.Rules.Services;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

// 为边界提升运行时的内部实现提供遗留覆盖测试。
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

    private static async Task<ModelAnalysis.AnalysisOutput> AnalyzeAsync(string sourceText) =>
        await ((ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine()).AnalyzeAsync(
            new ModelAnalysis.SourceDocumentSet(
                "Sample.cs",
                "Sample.cs",
                [
                    new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", sourceText)
                ]),
            CancellationToken.None);
}
