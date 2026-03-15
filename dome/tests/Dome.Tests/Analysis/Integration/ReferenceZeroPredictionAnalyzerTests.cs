using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

/// <summary>
/// 引用归零预测分析器测试。
/// </summary>
public class ReferenceZeroPredictionAnalyzerTests
{
    /// <summary>
    /// 测试 Predict 方法在唯一调用引用被计划删除时发出方法删除决策。
    /// </summary>
    [Fact]
    public async Task Predict_EmitsMethodDeleteWhenOnlyCallReferenceIsPlannedForDeletion()
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
        var statementTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "fun2(value);"));
        var decisions = new[]
        {
            MarkDecision.ForTarget(statementTarget.Target, PlanActionKind.Delete, "dome:delete", "delete call")
        };

        var predicted = new ReferenceZeroPredictionAnalyzer().Predict(context, decisions);

        var methodDelete = Assert.Single(predicted);
        Assert.Equal("reference-zero-prediction", methodDelete.Reason.RuleId);
        Assert.Equal(PlanActionKind.Delete, methodDelete.Action.Kind);
        Assert.Equal(TargetKind.Method, methodDelete.Target.TargetKind);
        Assert.Equal("Sample.Player.fun2(int)", methodDelete.Target.MemberId.Value);
    }

    /// <summary>
    /// 测试 Predict 方法在仍有其他调用引用时不会发出方法删除决策。
    /// </summary>
    [Fact]
    public async Task Predict_DoesNotEmitMethodDeleteWhenOtherCallReferencesRemain()
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

                        public void Update2(int value)
                        {
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
        var statementTarget = Assert.Single(analysis.View.Targets.Where(target =>
            target.Target.DisplayText == "fun2(value);" &&
            target.Target.MemberId.Value == "Sample.Player.Update(int)"));
        var decisions = new[]
        {
            MarkDecision.ForTarget(statementTarget.Target, PlanActionKind.Delete, "dome:delete", "delete call")
        };

        var predicted = new ReferenceZeroPredictionAnalyzer().Predict(context, decisions);

        Assert.Empty(predicted);
    }

    [Fact]
    public async Task Predict_AcceptsSnapshotServicesAndExecutionContext()
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
        var statementTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "fun2(value);"));
        var decisions = new[]
        {
            MarkDecision.ForTarget(statementTarget.Target, PlanActionKind.Delete, "dome:delete", "delete call")
        };

        var predicted = new ReferenceZeroPredictionAnalyzer().Predict(
            context.Snapshot,
            context.Services,
            new RuleExecutionContext("ReferenceZeroPredictionAnalyzerTests", statementTarget.Target, StatementScopeMode.MinimalBlock, CancellationToken.None, "verify explicit context"),
            decisions);

        var methodDelete = Assert.Single(predicted);
        Assert.Equal("reference-zero-prediction", methodDelete.Reason.RuleId);
        Assert.Equal("Sample.Player.fun2(int)", methodDelete.Target.MemberId.Value);
    }
}
