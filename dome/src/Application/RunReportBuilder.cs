namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;

/// <summary>
/// 运行报告构建器。
/// </summary>
public sealed class RunReportBuilder
{
    /// <summary>
    /// 构建工作区加载失败报告。
    /// </summary>
    public RunReport BuildWorkspaceLoadFailure(
        WorkspaceLoadResult loadResult,
        string message,
        IReadOnlyList<string> generatedArtifacts)
    {
        return new RunReport(
            false,
            FailureCode.WorkspaceLoadFailed,
            0,
            0,
            0,
            0,
            generatedArtifacts,
            new FailureSummary(FailureCode.WorkspaceLoadFailed, message),
            Array.Empty<ConflictSummary>(),
            new RiskSummary(0, Array.Empty<string>()),
            new PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            loadResult.Diagnostics,
            message);
    }

    /// <summary>
    /// 构建分析失败报告。
    /// </summary>
    public RunReport BuildAnalysisFailure(
        WorkspaceLoadResult loadResult,
        string message,
        IReadOnlyList<string> generatedArtifacts)
    {
        return new RunReport(
            false,
            FailureCode.AnalysisFailed,
            0,
            0,
            0,
            0,
            generatedArtifacts,
            new FailureSummary(FailureCode.AnalysisFailed, message),
            Array.Empty<ConflictSummary>(),
            new RiskSummary(0, Array.Empty<string>()),
            new PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            loadResult.Diagnostics,
            message);
    }

    /// <summary>
    /// 构建仅分析成功报告。
    /// </summary>
    public RunReport BuildAnalyzeOnlySuccess(
        AnalysisResultModel view,
        WorkspaceLoadResult loadResult,
        IReadOnlyList<string> generatedArtifacts,
        AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new RunReport(
            true,
            FailureCode.None,
            view.Targets.Count,
            0,
            0,
            0,
            generatedArtifacts,
            null,
            Array.Empty<ConflictSummary>(),
            BuildRiskSummary(view),
            new PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            loadResult.Diagnostics,
            null)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建计划编译失败报告。
    /// </summary>
    public RunReport BuildPlanCompileFailure(
        AnalysisResultModel view,
        WorkspaceLoadResult loadResult,
        PlanCompilationResult planResult,
        PlanCoverageSummary coverageSummary,
        FunctionImpactSet? functionImpactSet,
        IReadOnlyList<MarkDecision> initialDecisions,
        IReadOnlyList<MarkDecision> predictedDecisions,
        IReadOnlyList<string> generatedArtifacts,
        AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new RunReport(
            false,
            FailureCode.PlanCompileFailed,
            view.Targets.Count,
            0,
            planResult.Conflicts.Count,
            0,
            generatedArtifacts,
            new FailureSummary(FailureCode.PlanCompileFailed, planResult.Message ?? "Plan compilation failed."),
            BuildConflictSummaries(planResult.Conflicts),
            BuildRiskSummary(view),
            coverageSummary,
            BuildFunctionImpactSummary(functionImpactSet),
            BuildBoundaryPromotionSummary(initialDecisions),
            BuildReferenceZeroPredictionSummary(predictedDecisions),
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            loadResult.Diagnostics,
            planResult.Message)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建仅计划成功报告。
    /// </summary>
    public RunReport BuildPlanOnlySuccess(
        AnalysisResultModel view,
        WorkspaceLoadResult loadResult,
        IReadOnlyList<MarkDecision> decisions,
        AuditPlan plan,
        FunctionImpactSet? functionImpactSet,
        IReadOnlyList<string> generatedArtifacts,
        AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new RunReport(
            true,
            FailureCode.None,
            view.Targets.Count,
            plan.Changes.Count,
            plan.Conflicts.Count,
            0,
            generatedArtifacts,
            null,
            BuildConflictSummaries(plan.Conflicts),
            BuildRiskSummary(view),
            BuildPlanCoverageSummary(decisions, plan),
            BuildFunctionImpactSummary(functionImpactSet),
            BuildBoundaryPromotionSummary(decisions),
            BuildReferenceZeroPredictionSummary(decisions),
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            loadResult.Diagnostics,
            null)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建重写失败报告。
    /// </summary>
    public RunReport BuildRewriteFailure(
        AnalysisResultModel view,
        WorkspaceLoadResult loadResult,
        AuditPlan documentPlan,
        int rewrittenDocumentCount,
        PlanCoverageSummary coverageSummary,
        FunctionImpactSet? functionImpactSet,
        IReadOnlyList<MarkDecision> initialDecisions,
        IReadOnlyList<MarkDecision> predictedDecisions,
        string? message,
        IReadOnlyList<string> generatedArtifacts,
        AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new RunReport(
            false,
            FailureCode.RewriteFailed,
            view.Targets.Count,
            documentPlan.Changes.Count,
            documentPlan.Conflicts.Count,
            rewrittenDocumentCount,
            generatedArtifacts,
            new FailureSummary(FailureCode.RewriteFailed, message ?? "Rewrite failed."),
            BuildConflictSummaries(documentPlan.Conflicts),
            BuildRiskSummary(view),
            coverageSummary,
            BuildFunctionImpactSummary(functionImpactSet),
            BuildBoundaryPromotionSummary(initialDecisions),
            BuildReferenceZeroPredictionSummary(predictedDecisions),
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            loadResult.Diagnostics,
            message)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建标准成功报告。
    /// </summary>
    public RunReport BuildStandardSuccess(
        AnalysisResultModel view,
        WorkspaceLoadResult loadResult,
        IReadOnlyList<MarkDecision> decisions,
        AuditPlan plan,
        int rewrittenDocumentCount,
        FunctionImpactSet? functionImpactSet,
        IReadOnlyList<string> generatedArtifacts,
        AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new RunReport(
            true,
            FailureCode.None,
            view.Targets.Count,
            plan.Changes.Count,
            0,
            rewrittenDocumentCount,
            generatedArtifacts,
            null,
            BuildConflictSummaries(plan.Conflicts),
            BuildRiskSummary(view),
            BuildPlanCoverageSummary(decisions, plan),
            BuildFunctionImpactSummary(functionImpactSet),
            BuildBoundaryPromotionSummary(decisions),
            BuildReferenceZeroPredictionSummary(decisions),
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            loadResult.Diagnostics,
            null)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建冲突摘要列表。
    /// </summary>
    private static IReadOnlyList<ConflictSummary> BuildConflictSummaries(IReadOnlyList<PlanConflict> conflicts)
    {
        return conflicts
            .Select(conflict => new ConflictSummary(
                conflict.ConflictCode,
                conflict.Target.TargetKey,
                conflict.Target.DisplayText,
                conflict.ActionKinds,
                conflict.Reason))
            .ToArray();
    }

    /// <summary>
    /// 构建风险摘要。
    /// </summary>
    private static RiskSummary BuildRiskSummary(AnalysisResultModel view)
    {
        var skippedHighRiskTargets = view.Targets
            .Where(target => target.IsHighRisk && target.Directives.Count > 0)
            .Select(target => target.Target.DisplayText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new RiskSummary(
            skippedHighRiskTargets.Length,
            skippedHighRiskTargets.Take(5).ToArray());
    }

    /// <summary>
    /// 构建计划覆盖率摘要。
    /// </summary>
    private static PlanCoverageSummary BuildPlanCoverageSummary(
        IReadOnlyList<MarkDecision> decisions,
        AuditPlan plan)
    {
        var classDeleteIds = plan.Changes
            .Where(change => change.Target.TargetKind == TargetKind.Class && change.Action.Kind == PlanActionKind.Delete)
            .Select(change => $"{change.Target.DocumentPath}|{change.Target.MemberId.Value}")
            .ToArray();
        var plannedTargetKeys = plan.Changes
            .Select(change => change.Target.TargetKey)
            .ToHashSet(StringComparer.Ordinal);
        var covered = decisions
            .Where(decision => !plannedTargetKeys.Contains(decision.Target.TargetKey))
            .Where(decision => decision.Target.TargetKind is TargetKind.Method or TargetKind.Statement)
            .Where(decision => classDeleteIds.Any(classId => IsCoveredByClassDelete(decision.Target, classId)))
            .GroupBy(decision => decision.Target.TargetKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        return new PlanCoverageSummary(
            covered.Count(decision => decision.Target.TargetKind == TargetKind.Method),
            covered.Count(decision => decision.Target.TargetKind == TargetKind.Statement),
            covered.Select(decision => decision.Target.DisplayText)
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray());
    }

    /// <summary>
    /// 构建函数影响摘要。
    /// </summary>
    private static FunctionImpactSummary? BuildFunctionImpactSummary(FunctionImpactSet? impactSet)
    {
        if (impactSet == null || impactSet.DeletedFunctionIds.Count == 0)
        {
            return null;
        }

        return new FunctionImpactSummary(
            impactSet.DeletedFunctionIds.Count,
            impactSet.AffectedFunctionIds.Count,
            impactSet.AffectedDocumentPaths.Count,
            impactSet.ExpansionDepth,
            impactSet.EdgeKinds,
            impactSet.AffectedFunctionIds.Take(5).ToArray(),
            impactSet.AffectedDocumentPaths.Take(5).ToArray());
    }

    /// <summary>
    /// 构建引用归零预测摘要。
    /// </summary>
    private static ReferenceZeroPredictionSummary BuildReferenceZeroPredictionSummary(
        IReadOnlyList<MarkDecision> decisions)
    {
        var predictedMethods = decisions
            .Where(decision => decision.Reason.Origin == DecisionOrigin.Prediction)
            .Select(decision => decision.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new ReferenceZeroPredictionSummary(
            predictedMethods.Length,
            predictedMethods.Take(5).ToArray());
    }

    /// <summary>
    /// 构建边界提升摘要。
    /// </summary>
    private static BoundaryPromotionSummary BuildBoundaryPromotionSummary(
        IReadOnlyList<MarkDecision> decisions)
    {
        var promotedMethods = decisions
            .Where(decision => decision.Reason.Origin == DecisionOrigin.BoundaryPromotion)
            .Select(decision => decision.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new BoundaryPromotionSummary(
            BoundaryKind.Invocation,
            promotedMethods.Length,
            promotedMethods.Take(5).ToArray());
    }

    /// <summary>
    /// 判断目标是否被类删除覆盖。
    /// </summary>
    private static bool IsCoveredByClassDelete(PlanTarget target, string classDeleteId)
    {
        var separator = classDeleteId.IndexOf('|');
        if (separator < 0)
        {
            return false;
        }

        var documentPath = classDeleteId[..separator];
        var classId = classDeleteId[(separator + 1)..];
        return string.Equals(target.DocumentPath, documentPath, StringComparison.Ordinal) &&
               target.MemberId.Value.StartsWith($"{classId}.", StringComparison.Ordinal);
    }
}
