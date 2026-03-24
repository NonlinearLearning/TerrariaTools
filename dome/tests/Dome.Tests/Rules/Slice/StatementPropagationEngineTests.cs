using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;
using TerrariaTools.Dome.Core.Rules.Services;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

// 为遗留的内部传播引擎提供覆盖。
// 这些测试直接验证 BuildDecisions 之下的传播逻辑；公开规则契约由 BuildDecisions 测试覆盖。
public sealed class StatementPropagationEngineLegacyTests
{
    [Fact]
    public async Task Propagate_BuildsPropagationDecisionFromSeedStatement()
    {
        var analysis = await AnalyzeAsync(
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
            """);

        var context = analysis.CreateContext();
        var seedTarget = Assert.Single(context.View.Targets.Where(target => target.Locator.DisplayText == "int count = 1;"));
        var seedDecision = CreateDeleteDecision(seedTarget, "dome:delete", "seed");
        var seedDecisionsByTarget = new Dictionary<string, IReadOnlyList<ModelRules.MarkDecision>>(StringComparer.Ordinal)
        {
            [seedDecision.TargetKey] = [seedDecision]
        };

        var propagated = new StatementPropagationEngine(MarkingRuleRegistry.CreateDefault()).Propagate(
            context,
            new ModelRules.RuleExecutionContext("StatementPropagationEngineTests", seedTarget.Target, ModelPrimitives.StatementScopeMode.MinimalBlock, CancellationToken.None, "direct propagation"),
            seedTarget,
            seedDecisionsByTarget);

        var propagatedDecision = Assert.Single(propagated);
        Assert.Equal("int next = count;", propagatedDecision.Locator.DisplayText);
        Assert.Equal("dataflow-propagation", propagatedDecision.Reason.RuleId);
        Assert.NotNull(propagatedDecision.Chain);
    }

    // Legacy internal engine coverage for statement scope selection. The public
    // contract proof for this behavior lives in BuildDecisions tests.
    [Fact]
    public async Task ParentBlockPiercingScopeRule_ExpandsSnapshotWhenExplicitlyRequired()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update(int seed)
                {
                    int parent = seed;
                    {
                        // dome:delete
                        int child = parent;
                        int next = child;
                    }
                }
            }
            """);

        var context = analysis.CreateContext();
        var recordingStatements = new RecordingStatementAnalysisService(context.Statements);
        var patchedContext = ModelAnalysis.AnalysisContext.Create(context.Snapshot, context.Services with { Statements = recordingStatements });
        var seedTarget = Assert.Single(context.View.Targets.Where(target => target.Locator.DisplayText == "int child = parent;"));
        var seedDecision = CreateDeleteDecision(seedTarget, "dome:delete", "seed");
        var seedDecisionsByTarget = new Dictionary<string, IReadOnlyList<ModelRules.MarkDecision>>(StringComparer.Ordinal)
        {
            [seedDecision.TargetKey] = [seedDecision]
        };

        var propagated = new StatementPropagationEngine(MarkingRuleRegistry.CreateDefault()).Propagate(
            patchedContext,
            new ModelRules.RuleExecutionContext("StatementPropagationEngineTests", seedTarget.Target, ModelPrimitives.StatementScopeMode.ParentBlockPiercing, CancellationToken.None, "explicit scope"),
            seedTarget,
            seedDecisionsByTarget);

        Assert.Contains(propagated, decision => decision.Locator.DisplayText == "int next = child;");
        Assert.Contains(
            recordingStatements.Calls,
            call => call.TargetKey == seedDecision.TargetKey && call.ScopeMode == ModelPrimitives.StatementScopeMode.ParentBlockPiercing);
    }

    [Fact]
    public async Task Propagate_StopsAtProtectedObjectInitializerBoundary()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Item
            {
                public int Value { get; set; }
            }

            public class Player
            {
                public void Update(int seed)
                {
                    // dome:delete
                    int count = seed;
                    var item = new Item { Value = count };
                    int next = count;
                }
            }
            """);

        var context = analysis.CreateContext();
        var seedTarget = Assert.Single(context.View.Targets.Where(target => target.Locator.DisplayText == "int count = seed;"));
        var seedDecision = CreateDeleteDecision(seedTarget, "dome:delete", "seed");
        var seedDecisionsByTarget = new Dictionary<string, IReadOnlyList<ModelRules.MarkDecision>>(StringComparer.Ordinal)
        {
            [seedDecision.TargetKey] = [seedDecision]
        };

        var propagated = new StatementPropagationEngine(MarkingRuleRegistry.CreateDefault()).Propagate(
            context,
            new ModelRules.RuleExecutionContext("StatementPropagationEngineTests", seedTarget.Target, ModelPrimitives.StatementScopeMode.MinimalBlock, CancellationToken.None, "protection boundary"),
            seedTarget,
            seedDecisionsByTarget);

        Assert.DoesNotContain(propagated, decision => decision.Locator.DisplayText == "int next = count;");
    }

    private static ModelRules.MarkDecision CreateDeleteDecision(ModelAnalysis.AnalysisTarget target, string ruleId, string reasonText) =>
        new(
            target.Target,
            target.Locator,
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete, null),
            new ModelRules.PlanReason(ruleId, reasonText));

    private static async Task<ModelAnalysis.AnalysisOutput> AnalyzeAsync(string sourceText) =>
        await ((ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine()).AnalyzeAsync(
            new ModelAnalysis.SourceDocumentSet(
                "Sample.cs",
                "Sample.cs",
                [
                    new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", sourceText)
                ]),
            CancellationToken.None);

    private sealed class RecordingStatementAnalysisService(ModelAnalysis.IStatementAnalysisService inner) : ModelAnalysis.IStatementAnalysisService
    {
        public List<(string TargetKey, ModelPrimitives.StatementScopeMode ScopeMode)> Calls { get; } = [];

        public ModelAnalysis.StatementGraphSnapshot Analyze(string targetKey) =>
            Analyze(targetKey, ModelPrimitives.StatementScopeMode.MinimalBlock);

        public ModelAnalysis.StatementGraphSnapshot Analyze(string targetKey, ModelPrimitives.StatementScopeMode scopeMode)
        {
            Calls.Add((targetKey, scopeMode));
            return inner.Analyze(targetKey, scopeMode);
        }
    }
}
