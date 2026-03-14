using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public class StatementPropagationEngineTests
{
    [Fact]
    public async Task Propagate_BuildsPropagationDecisionFromSeedStatement()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
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
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var seedTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "int count = 1;"));
        var seedDecision = MarkDecision.ForTarget(seedTarget.Target, PlanActionKind.Delete, "dome:delete", "seed");
        var seedDecisionsByTarget = new Dictionary<string, IReadOnlyList<MarkDecision>>(StringComparer.Ordinal)
        {
            [seedTarget.Target.TargetKey] = new[] { seedDecision }
        };

        var propagated = new StatementPropagationEngine(MarkingRuleRegistry.CreateDefault()).Propagate(
            context,
            new RuleExecutionContext("StatementPropagationEngineTests", seedTarget.Target, StatementScopeMode.MinimalBlock, CancellationToken.None, "direct propagation"),
            seedTarget,
            seedDecisionsByTarget);

        var propagatedDecision = Assert.Single(propagated);
        Assert.Equal("int next = count;", propagatedDecision.Target.DisplayText);
        Assert.Equal("dataflow-propagation", propagatedDecision.Reason.RuleId);
        Assert.NotNull(propagatedDecision.Chain);
    }

    // Rule family: IStatementScopeRule
    // Direct behavior: the seed statement remains the direct decision root.
    // Propagation: explicit ParentBlockPiercing expands the statement snapshot to the parent block.
    // Blocking: expansion still cannot cross the function boundary.
    // Boundary promotion: scope changes affect visibility only and do not change promotion semantics.
    [Fact]
    public async Task ParentBlockPiercingScopeRule_ExpandsSnapshotWhenExplicitlyRequired()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
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
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var recordingStatements = new RecordingStatementAnalysisService(context.Statements);
        var patchedContext = AnalysisContext.Create(context.Snapshot, context.Services with { Statements = recordingStatements });
        var seedTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "int child = parent;"));
        var seedDecision = MarkDecision.ForTarget(seedTarget.Target, PlanActionKind.Delete, "dome:delete", "seed");
        var seedDecisionsByTarget = new Dictionary<string, IReadOnlyList<MarkDecision>>(StringComparer.Ordinal)
        {
            [seedTarget.Target.TargetKey] = new[] { seedDecision }
        };

        var propagated = new StatementPropagationEngine(MarkingRuleRegistry.CreateDefault()).Propagate(
            patchedContext,
            new RuleExecutionContext("StatementPropagationEngineTests", seedTarget.Target, StatementScopeMode.ParentBlockPiercing, CancellationToken.None, "explicit scope"),
            seedTarget,
            seedDecisionsByTarget);

        Assert.Contains(propagated, decision => decision.Target.DisplayText == "int next = child;");
        Assert.Contains(
            recordingStatements.Calls,
            call => call.TargetKey == seedTarget.Target.TargetKey && call.ScopeMode == StatementScopeMode.ParentBlockPiercing);
    }

    [Fact]
    public async Task Propagate_StopsAtProtectedObjectInitializerBoundary()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
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
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var seedTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "int count = seed;"));
        var seedDecision = MarkDecision.ForTarget(seedTarget.Target, PlanActionKind.Delete, "dome:delete", "seed");
        var seedDecisionsByTarget = new Dictionary<string, IReadOnlyList<MarkDecision>>(StringComparer.Ordinal)
        {
            [seedTarget.Target.TargetKey] = new[] { seedDecision }
        };

        var propagated = new StatementPropagationEngine(MarkingRuleRegistry.CreateDefault()).Propagate(
            context,
            new RuleExecutionContext("StatementPropagationEngineTests", seedTarget.Target, StatementScopeMode.MinimalBlock, CancellationToken.None, "protection boundary"),
            seedTarget,
            seedDecisionsByTarget);

        Assert.DoesNotContain(propagated, decision => decision.Target.DisplayText == "int next = count;");
    }

    private sealed class RecordingStatementAnalysisService(IStatementAnalysisService inner) : IStatementAnalysisService
    {
        public List<(string TargetKey, StatementScopeMode ScopeMode)> Calls { get; } = new();

        public StatementGraphSnapshot Analyze(PlanTarget seedTarget, StatementScopeMode scopeMode)
        {
            Calls.Add((seedTarget.TargetKey, scopeMode));
            return inner.Analyze(seedTarget, scopeMode);
        }
    }
}
