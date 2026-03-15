using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class RunReportBuilderTests
{
    [Fact]
    public void BuildAnalyzeOnlySuccess_ProjectsRiskSummaryAndArtifacts()
    {
        var builder = new RunReportBuilder();
        var view = new AnalysisResultModel(
            new[]
            {
                new AnalysisTarget(
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Update()"), MemberKind.Method, TargetKind.Statement, 0, 1, "Run();"),
                    true,
                    new[] { new DirectiveAction(PlanActionKind.Delete, null, "directive", "delete") },
                    Array.Empty<SymbolRef>(),
                    Array.Empty<SymbolRef>(),
                    Array.Empty<MemberId>(),
                    StatementKindRef.Unknown,
                    false,
                    false,
                    false,
                    Array.Empty<string>(),
                    StatementScopeMode.MinimalBlock,
                    "scope",
                    null)
            },
            Array.Empty<AnalysisEdge>(),
            new TypeDependencyGraph(Array.Empty<TypeNodeRef>(), Array.Empty<TypeDependencyEdge>()),
            new FunctionDependencyGraph(Array.Empty<FunctionNodeRef>(), Array.Empty<FunctionDependencyEdge>()),
            new StatementDependencyGraph(Array.Empty<string>(), Array.Empty<StatementDependencyEdge>()),
            StatementGraphMaterialization.SnapshotOnly,
            FunctionGraphMaterialization.None);
        var loadResult = WorkspaceLoadResult.Success(
            new[]
            {
                new SourceDocument("Sample.cs", "Sample.cs", "class C {}")
            },
            WorkspaceLoadMode.SourceOnly,
            "SourceOnly");

        var report = builder.BuildAnalyzeOnlySuccess(view, loadResult, new[] { "analysis.json", "report.json" });

        Assert.True(report.IsSuccess);
        Assert.Equal(1, report.AnalysisTargets);
        Assert.Equal(2, report.GeneratedArtifacts.Count);
        Assert.Equal(1, report.RiskSummary.SkippedHighRiskTargetCount);
        Assert.Null(report.FunctionImpactSummary);
    }

    [Fact]
    public void BuildPlanOnlySuccess_ProjectsCoverageAndPredictionSummaries()
    {
        var builder = new RunReportBuilder();
        var view = new AnalysisResultModel(
            Array.Empty<AnalysisTarget>(),
            Array.Empty<AnalysisEdge>(),
            new TypeDependencyGraph(Array.Empty<TypeNodeRef>(), Array.Empty<TypeDependencyEdge>()),
            new FunctionDependencyGraph(Array.Empty<FunctionNodeRef>(), Array.Empty<FunctionDependencyEdge>()),
            new StatementDependencyGraph(Array.Empty<string>(), Array.Empty<StatementDependencyEdge>()),
            StatementGraphMaterialization.SnapshotOnly,
            FunctionGraphMaterialization.None);
        var loadResult = WorkspaceLoadResult.Success(
            new[]
            {
                new SourceDocument("Sample.cs", "Sample.cs", "class C {}")
            },
            WorkspaceLoadMode.SourceOnly,
            "SourceOnly");
        var methodTarget = new PlanTarget("Sample.cs", new MemberId("Sample.Player.Run()"), MemberKind.Method, TargetKind.Method, 0, 5, "Run");
        var statementTarget = new PlanTarget("Sample.cs", new MemberId("Sample.Player.Run()"), MemberKind.Method, TargetKind.Statement, 10, 5, "Run();");
        var decisions = new[]
        {
            MarkDecision.ForTarget(
                methodTarget,
                PlanActionKind.Delete,
                "boundary-promotion",
                "promoted"),
            MarkDecision.ForTarget(
                methodTarget,
                PlanActionKind.Delete,
                "reference-zero-prediction",
                "predicted"),
            MarkDecision.ForTarget(
                statementTarget,
                PlanActionKind.CommentOut,
                "seed",
                "covered")
        };
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "in", "out", RunMode.PlanOnly),
            new[]
            {
                new PlannedChange(0, new PlanTarget("Sample.cs", new MemberId("Sample.Player"), MemberKind.Class, TargetKind.Class, 0, 1, "Player"), new PlanAction(PlanActionKind.Delete), new PlanReason("class-mark", "delete class"))
            },
            Array.Empty<PlanConflict>());
        var impact = new FunctionImpactSet(
            new[] { "Sample.Player.Run()" },
            new[] { "Sample.Player.Ping()" },
            new[] { "Sample.cs" },
            1,
            new[] { FunctionDependencyKind.Calls });

        var report = builder.BuildPlanOnlySuccess(
            view,
            loadResult,
            decisions,
            plan,
            impact,
            new[] { "audit-plan.json", "report.json" });

        Assert.True(report.IsSuccess);
        Assert.NotNull(report.FunctionImpactSummary);
        Assert.NotNull(report.BoundaryPromotionSummary);
        Assert.NotNull(report.ReferenceZeroPredictionSummary);
        Assert.Equal(2, report.PlanCoverageSummary.CoveredMethodCount);
        Assert.Equal(1, report.PlanCoverageSummary.CoveredStatementCount);
        Assert.Equal(1, report.BoundaryPromotionSummary!.PromotedMethodDeleteCount);
        Assert.Equal(1, report.ReferenceZeroPredictionSummary!.PredictedMethodDeleteCount);
    }
}
