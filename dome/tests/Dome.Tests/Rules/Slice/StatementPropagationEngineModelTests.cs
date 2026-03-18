using TerrariaTools.Dome.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public sealed class StatementPropagationEngineModelTests
{
    [Fact]
    public async Task Propagate_UsesModelContextAndReturnsModelDecisions()
    {
        var analysis = await ((ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine()).AnalyzeAsync(
            new ApplicationAbstractions.SourceDocumentSet(
                "Sample.cs",
                "Sample.cs",
                [
                    new ApplicationAbstractions.SourceDocument(
                        "Sample.cs",
                        "Sample.cs",
                        """
                        namespace Sample;

                        public class Player
                        {
                            public void Update()
                            {
                                // dome:delete
                                int count = 1;
                                int next = count;
                            }
                        }
                        """)
                ]),
            CancellationToken.None);

        var context = analysis.CreateContext();
        var seedTarget = Assert.Single(context.View.Targets.Where(target => target.Locator.DisplayText == "int count = 1;"));
        var seedDecision = new ModelRules.MarkDecision(
            seedTarget.Target,
            seedTarget.Locator,
            new TerrariaTools.Dome.Model.Planning.PlanAction(ModelPrimitives.PlanActionKind.Delete, null),
            new ModelRules.PlanReason("dome:delete", "seed"));
        var seedDecisionsByTarget = new Dictionary<string, IReadOnlyList<ModelRules.MarkDecision>>(StringComparer.Ordinal)
        {
            [seedDecision.TargetKey] = [seedDecision]
        };

        var propagated = new StatementPropagationEngine(MarkingRuleRegistry.CreateDefault()).Propagate(
            context,
            new ModelRules.RuleExecutionContext(
                "StatementPropagationEngineModelTests",
                seedTarget.Target,
                ModelPrimitives.StatementScopeMode.MinimalBlock,
                CancellationToken.None,
                "direct propagation"),
            seedTarget,
            seedDecisionsByTarget);

        var propagatedDecision = Assert.Single(propagated);
        Assert.Equal("int next = count;", propagatedDecision.Locator.DisplayText);
        Assert.Equal("dataflow-propagation", propagatedDecision.Reason.RuleId);
        Assert.NotNull(propagatedDecision.Chain);
    }
}
