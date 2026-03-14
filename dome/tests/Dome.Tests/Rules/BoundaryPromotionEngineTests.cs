using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public class BoundaryPromotionEngineTests
{
    // Rule family: IBoundaryPromotionRule
    // Direct behavior: a qualifying statement delete remains a statement-level decision first.
    // Propagation: promotion consumes the existing statement decision rather than creating a new propagation source.
    // Blocking: if other references remain, promotion should not happen.
    // Boundary promotion: a single remaining private invocation is promoted to a method delete.
    [Fact]
    public async Task InvocationBoundaryPromotionRule_PromotesSingleStatementDeleteToMethodDelete()
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
                        public void Update(int value)
                        {
                            // dome:delete
                            fun2(value);
                        }

                        private void fun2(int i)
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var statementDecision = Assert.Single(
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
                .Execute(analysis.View)
                .Where(decision => decision.Target.TargetKind == TargetKind.Statement));

        var promoted = new BoundaryPromotionEngine(MarkingRuleRegistry.CreateDefault()).Promote(
            context,
            new[] { statementDecision },
            context.View.Targets.ToDictionary(target => target.Target.TargetKey, StringComparer.Ordinal));

        var promotedDecision = Assert.Single(promoted);
        Assert.Equal("boundary-promotion", promotedDecision.Reason.RuleId);
        Assert.Equal(TargetKind.Method, promotedDecision.Target.TargetKind);
        Assert.Equal("Sample.Player.fun2(int)", promotedDecision.Target.MemberId.Value);
    }

    [Fact]
    public async Task InvocationBoundaryPromotionRule_DoesNotPromotePropagatedDelete()
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
                        public void Update(int value)
                        {
                            int count = value;
                            fun2(count);
                        }

                        private void fun2(int i)
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var invocationTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "fun2(count);"));
        var propagatedDelete = MarkDecision.ForTarget(
            invocationTarget.Target,
            PlanActionKind.Delete,
            "dataflow-propagation",
            "propagated",
            sourceTargetKey: "seed",
            sourceTargetDisplayText: "int count = value;");

        var promoted = new BoundaryPromotionEngine(MarkingRuleRegistry.CreateDefault()).Promote(
            context,
            new[] { propagatedDelete },
            context.View.Targets.ToDictionary(target => target.Target.TargetKey, StringComparer.Ordinal));

        Assert.Empty(promoted);
    }
}
