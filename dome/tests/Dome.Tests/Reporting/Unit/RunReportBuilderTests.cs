using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Application;
using TerrariaTools.Testing.GoldenOutputs;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class RunReportBuilderTests
{
    [Fact]
    public Task BuildPlanOnlySuccess_MatchesGoldenReportShape()
    {
        var builder = new RunReportBuilder();
        var view = CreateEmptyView();
        var loadResult = CreateLoadResult();
        var target = CreateTarget(ModelPrimitives.TargetKind.Method, "Sample.Player.Run()", 0, 5, "Run");
        var decisions = new[]
        {
            CreateDecision(target, ModelPrimitives.PlanActionKind.Delete, "boundary-promotion", "promoted", ModelPrimitives.DecisionOrigin.BoundaryPromotion),
            CreateDecision(target, ModelPrimitives.PlanActionKind.ChangeVisibilityToPrivate, "member-cleanup", "privatized", ModelPrimitives.DecisionOrigin.Rule)
        };
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.PlanOnly),
            new[]
            {
                new ModelPlanning.PlannedChange(
                    0,
                    target.target,
                    target.locator,
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                    new ModelRules.PlanReason("class-mark", "delete method"))
            },
            Array.Empty<ModelPlanning.PlanConflict>());

        var report = builder.BuildPlanOnlySuccess(
            view,
            loadResult,
            decisions,
            plan,
            null,
            new[] { "audit-plan.json", "report.json" });

        return VerifyCompatibilitySettingsFixture.VerifyJson(report);
    }

    [Fact]
    public void BuildAnalyzeOnlySuccess_ProjectsRiskSummaryAndArtifacts()
    {
        var builder = new RunReportBuilder();
        var riskyTarget = CreateTarget(ModelPrimitives.TargetKind.Statement, "Sample.Player.Update()", 0, 1, "Run();");
        var view = new ModelAnalysis.AnalysisResultModel(
            new[]
            {
                new ModelAnalysis.AnalysisTarget(
                    riskyTarget.target,
                    riskyTarget.locator,
                    true,
                    new[] { new ModelRules.DirectiveAction(ModelPrimitives.PlanActionKind.Delete, null, "directive", "delete") },
                    Array.Empty<ModelAnalysis.SymbolRef>(),
                    Array.Empty<ModelAnalysis.SymbolRef>(),
                    Array.Empty<ModelPrimitives.MemberId>(),
                    ModelPrimitives.StatementKindRef.Unknown,
                    false,
                    false,
                    false,
                    Array.Empty<string>(),
                    ModelPrimitives.StatementScopeMode.MinimalBlock,
                    "scope",
                    null)
            },
            Array.Empty<ModelAnalysis.AnalysisEdge>(),
            new ModelAnalysis.TypeDependencyGraph(Array.Empty<ModelAnalysis.TypeNodeRef>(), Array.Empty<ModelAnalysis.TypeDependencyEdge>()),
            new ModelAnalysis.FunctionDependencyGraph(Array.Empty<ModelAnalysis.FunctionNodeRef>(), Array.Empty<ModelAnalysis.FunctionDependencyEdge>()),
            new ModelAnalysis.StatementDependencyGraph(Array.Empty<string>(), Array.Empty<ModelAnalysis.StatementDependencyEdge>()),
            ModelPrimitives.StatementGraphMaterialization.SnapshotOnly,
            ModelPrimitives.FunctionGraphMaterialization.None);

        var report = builder.BuildAnalyzeOnlySuccess(view, CreateLoadResult(), new[] { "analysis.json", "report.json" });

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
        var view = CreateEmptyView();
        var loadResult = CreateLoadResult();
        var methodTarget = CreateTarget(ModelPrimitives.TargetKind.Method, "Sample.Player.Run()", 0, 5, "Run");
        var statementTarget = CreateTarget(ModelPrimitives.TargetKind.Statement, "Sample.Player.Run()", 10, 5, "Run();");
        var decisions = new[]
        {
            CreateDecision(methodTarget, ModelPrimitives.PlanActionKind.Delete, "boundary-promotion", "promoted", ModelPrimitives.DecisionOrigin.BoundaryPromotion),
            CreateDecision(methodTarget, ModelPrimitives.PlanActionKind.Delete, "reference-zero-prediction", "predicted", ModelPrimitives.DecisionOrigin.Prediction),
            CreateDecision(statementTarget, ModelPrimitives.PlanActionKind.CommentOut, "seed", "covered")
        };
        var classTarget = CreateTarget(ModelPrimitives.TargetKind.Class, "Sample.Player", 0, 1, "Player");
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.PlanOnly),
            new[]
            {
                new ModelPlanning.PlannedChange(
                    0,
                    classTarget.target,
                    classTarget.locator,
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                    new ModelRules.PlanReason("class-mark", "delete class"))
            },
            Array.Empty<ModelPlanning.PlanConflict>());
        var impact = new ModelPlanning.FunctionImpactSet(
            new[] { "Sample.Player.Run()" },
            new[] { "Sample.Player.Ping()" },
            new[] { "Sample.cs" },
            1,
            new[] { ModelPrimitives.FunctionDependencyKind.Calls });

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
        Assert.Equal(1, report.PlanCoverageSummary.CoveredMethodCount);
        Assert.Equal(1, report.PlanCoverageSummary.CoveredStatementCount);
        Assert.Equal(1, report.BoundaryPromotionSummary!.PromotedMethodDeleteCount);
        Assert.Equal(1, report.ReferenceZeroPredictionSummary!.PredictedMethodDeleteCount);
    }

    [Fact]
    public void BuildPlanOnlySuccess_UsesDecisionOriginInsteadOfRuleIdStrings()
    {
        var builder = new RunReportBuilder();
        var methodTarget = CreateTarget(ModelPrimitives.TargetKind.Method, "Sample.Player.Run()", 0, 5, "Run");
        var decisions = new[]
        {
            CreateDecision(methodTarget, ModelPrimitives.PlanActionKind.Delete, "renamed-boundary-rule", "promoted", ModelPrimitives.DecisionOrigin.BoundaryPromotion),
            CreateDecision(methodTarget, ModelPrimitives.PlanActionKind.Delete, "renamed-prediction-rule", "predicted", ModelPrimitives.DecisionOrigin.Prediction)
        };
        var report = builder.BuildPlanOnlySuccess(
            CreateEmptyView(),
            CreateLoadResult(),
            decisions,
            new ModelPlanning.AuditPlan(
                new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.PlanOnly),
                Array.Empty<ModelPlanning.PlannedChange>(),
                Array.Empty<ModelPlanning.PlanConflict>()),
            null,
            new[] { "audit-plan.json", "report.json" });

        Assert.Equal(1, report.BoundaryPromotionSummary!.PromotedMethodDeleteCount);
        Assert.Equal(1, report.ReferenceZeroPredictionSummary!.PredictedMethodDeleteCount);
    }

    [Fact]
    public void BuildAnalyzeOnlySuccess_ProjectsAdvancedAnalysisSummary()
    {
        var builder = new RunReportBuilder();
        var summary = new ModelAnalysis.AdvancedAnalysisSummary(
            PersistentTypeCount: 3,
            RiskyTypeCount: 1,
            Notes: new[] { "MethodRoots=1", "HotSymbol=Sample.Worker" });

        var report = builder.BuildAnalyzeOnlySuccess(CreateEmptyView(), CreateLoadResult(), new[] { "analysis.json", "report.json" }, summary);

        Assert.NotNull(report.AdvancedAnalysisSummary);
        Assert.Equal(3, report.AdvancedAnalysisSummary!.PersistentTypeCount);
        Assert.Equal(1, report.AdvancedAnalysisSummary.RiskyTypeCount);
        Assert.Contains("HotSymbol=Sample.Worker", report.AdvancedAnalysisSummary.Notes ?? Array.Empty<string>());
    }

    private static ModelAnalysis.AnalysisResultModel CreateEmptyView() =>
        new(
            Array.Empty<ModelAnalysis.AnalysisTarget>(),
            Array.Empty<ModelAnalysis.AnalysisEdge>(),
            new ModelAnalysis.TypeDependencyGraph(Array.Empty<ModelAnalysis.TypeNodeRef>(), Array.Empty<ModelAnalysis.TypeDependencyEdge>()),
            new ModelAnalysis.FunctionDependencyGraph(Array.Empty<ModelAnalysis.FunctionNodeRef>(), Array.Empty<ModelAnalysis.FunctionDependencyEdge>()),
            new ModelAnalysis.StatementDependencyGraph(Array.Empty<string>(), Array.Empty<ModelAnalysis.StatementDependencyEdge>()),
            ModelPrimitives.StatementGraphMaterialization.SnapshotOnly,
            ModelPrimitives.FunctionGraphMaterialization.None);

    private static ApplicationAbstractions.WorkspaceLoadResult CreateLoadResult() =>
        ApplicationAbstractions.WorkspaceLoadResult.Success(
            new ApplicationAbstractions.SourceDocumentSet(
                "Sample.cs",
                ".",
                new[] { new ApplicationAbstractions.SourceDocument("Sample.cs", "Sample.cs", "class C {}") }),
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            "SourceOnly");

    private static (ModelPrimitives.TargetIdentity target, ModelPrimitives.TargetLocator locator) CreateTarget(
        ModelPrimitives.TargetKind targetKind,
        string memberId,
        int spanStart,
        int spanLength,
        string displayText) =>
        (
            new ModelPrimitives.TargetIdentity("Sample.cs", new ModelPrimitives.MemberId(memberId), targetKind == ModelPrimitives.TargetKind.Class ? ModelPrimitives.MemberKind.Class : ModelPrimitives.MemberKind.Method, targetKind),
            new ModelPrimitives.TargetLocator(spanStart, spanLength, displayText));

    private static ModelRules.MarkDecision CreateDecision(
        (ModelPrimitives.TargetIdentity target, ModelPrimitives.TargetLocator locator) value,
        ModelPrimitives.PlanActionKind actionKind,
        string ruleId,
        string reasonText,
        ModelPrimitives.DecisionOrigin origin = ModelPrimitives.DecisionOrigin.Rule) =>
        new(
            value.target,
            value.locator,
            new ModelPlanning.PlanAction(actionKind),
            new ModelRules.PlanReason(ruleId, reasonText, Origin: origin));
}

