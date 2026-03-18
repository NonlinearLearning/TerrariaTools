namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

/// <summary>
/// 运行报告构建器。
/// </summary>
public sealed class RunReportBuilder
{
    /// <summary>
    /// 构建工作区加载失败报告。
    /// </summary>
    public ApplicationAbstractions.RunReport BuildWorkspaceLoadFailure(
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        string message,
        IReadOnlyList<string> generatedArtifacts)
    {
        return new ApplicationAbstractions.RunReport(
            false,
            ModelPrimitives.FailureCode.WorkspaceLoadFailed,
            0,
            0,
            0,
            0,
            generatedArtifacts,
            new ApplicationAbstractions.FailureSummary(ModelPrimitives.FailureCode.WorkspaceLoadFailed, message),
            Array.Empty<ApplicationAbstractions.ConflictSummary>(),
            new ApplicationAbstractions.RiskSummary(0, Array.Empty<string>()),
            new ApplicationAbstractions.PlanCoverageSummary(0, 0, Array.Empty<string>()),
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
    public ApplicationAbstractions.RunReport BuildAnalysisFailure(
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        string message,
        IReadOnlyList<string> generatedArtifacts)
    {
        return new ApplicationAbstractions.RunReport(
            false,
            ModelPrimitives.FailureCode.AnalysisFailed,
            0,
            0,
            0,
            0,
            generatedArtifacts,
            new ApplicationAbstractions.FailureSummary(ModelPrimitives.FailureCode.AnalysisFailed, message),
            Array.Empty<ApplicationAbstractions.ConflictSummary>(),
            new ApplicationAbstractions.RiskSummary(0, Array.Empty<string>()),
            new ApplicationAbstractions.PlanCoverageSummary(0, 0, Array.Empty<string>()),
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
    public ApplicationAbstractions.RunReport BuildAnalyzeOnlySuccess(
        ModelAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        IReadOnlyList<string> generatedArtifacts,
        ModelAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ApplicationAbstractions.RunReport(
            true,
            ModelPrimitives.FailureCode.None,
            view.Targets.Count,
            0,
            0,
            0,
            generatedArtifacts,
            null,
            Array.Empty<ApplicationAbstractions.ConflictSummary>(),
            BuildRiskSummary(view),
            new ApplicationAbstractions.PlanCoverageSummary(0, 0, Array.Empty<string>()),
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
    public ApplicationAbstractions.RunReport BuildPlanCompileFailure(
        ModelAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        ModelPlanning.PlanCompilationResult planResult,
        ApplicationAbstractions.PlanCoverageSummary coverageSummary,
        ModelPlanning.FunctionImpactSet? functionImpactSet,
        IReadOnlyList<ModelRules.MarkDecision> initialDecisions,
        IReadOnlyList<ModelRules.MarkDecision> predictedDecisions,
        IReadOnlyList<string> generatedArtifacts,
        ModelAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ApplicationAbstractions.RunReport(
            false,
            ModelPrimitives.FailureCode.PlanCompileFailed,
            view.Targets.Count,
            0,
            planResult.Conflicts.Count,
            0,
            generatedArtifacts,
            new ApplicationAbstractions.FailureSummary(ModelPrimitives.FailureCode.PlanCompileFailed, planResult.Message ?? "Plan compilation failed."),
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
    public ApplicationAbstractions.RunReport BuildPlanOnlySuccess(
        ModelAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        IReadOnlyList<ModelRules.MarkDecision> decisions,
        ModelPlanning.AuditPlan plan,
        ModelPlanning.FunctionImpactSet? functionImpactSet,
        IReadOnlyList<string> generatedArtifacts,
        ModelAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ApplicationAbstractions.RunReport(
            true,
            ModelPrimitives.FailureCode.None,
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
    public ApplicationAbstractions.RunReport BuildRewriteFailure(
        ModelAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        ModelPlanning.AuditPlan documentPlan,
        int rewrittenDocumentCount,
        ApplicationAbstractions.PlanCoverageSummary coverageSummary,
        ModelPlanning.FunctionImpactSet? functionImpactSet,
        IReadOnlyList<ModelRules.MarkDecision> initialDecisions,
        IReadOnlyList<ModelRules.MarkDecision> predictedDecisions,
        string? message,
        IReadOnlyList<string> generatedArtifacts,
        ModelAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ApplicationAbstractions.RunReport(
            false,
            ModelPrimitives.FailureCode.RewriteFailed,
            view.Targets.Count,
            documentPlan.Changes.Count,
            documentPlan.Conflicts.Count,
            rewrittenDocumentCount,
            generatedArtifacts,
            new ApplicationAbstractions.FailureSummary(ModelPrimitives.FailureCode.RewriteFailed, message ?? "Rewrite failed."),
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
    public ApplicationAbstractions.RunReport BuildStandardSuccess(
        ModelAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        IReadOnlyList<ModelRules.MarkDecision> decisions,
        ModelPlanning.AuditPlan plan,
        int rewrittenDocumentCount,
        ModelPlanning.FunctionImpactSet? functionImpactSet,
        IReadOnlyList<string> generatedArtifacts,
        ModelAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ApplicationAbstractions.RunReport(
            true,
            ModelPrimitives.FailureCode.None,
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
    private static IReadOnlyList<ApplicationAbstractions.ConflictSummary> BuildConflictSummaries(IReadOnlyList<ModelPlanning.PlanConflict> conflicts)
    {
        return conflicts
            .Select(conflict => new ApplicationAbstractions.ConflictSummary(
                conflict.ConflictCode,
                $"{conflict.Target.IdentityKey}|{conflict.Locator.EffectiveResolutionKey.SpanStart}|{conflict.Locator.EffectiveResolutionKey.SpanLength}",
                conflict.Locator.DisplayText,
                conflict.ActionKinds,
                conflict.Reason))
            .ToArray();
    }

    /// <summary>
    /// 构建风险摘要。
    /// </summary>
    private static ApplicationAbstractions.RiskSummary BuildRiskSummary(ModelAnalysis.AnalysisResultModel view)
    {
        var skippedHighRiskTargets = view.Targets
            .Where(target => target.IsHighRisk && target.Directives.Count > 0)
            .Select(target => target.Locator.DisplayText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ApplicationAbstractions.RiskSummary(
            skippedHighRiskTargets.Length,
            skippedHighRiskTargets.Take(5).ToArray());
    }

    /// <summary>
    /// 构建计划覆盖率摘要。
    /// </summary>
    private static ApplicationAbstractions.PlanCoverageSummary BuildPlanCoverageSummary(
        IReadOnlyList<ModelRules.MarkDecision> decisions,
        ModelPlanning.AuditPlan plan)
    {
        var classDeleteIds = plan.Changes
            .Where(change => change.Target.TargetKind == ModelPrimitives.TargetKind.Class && change.Action.Kind == ModelPrimitives.PlanActionKind.Delete)
            .Select(change => $"{change.Target.DocumentPath}|{change.Target.MemberId.Value}")
            .ToArray();
        var plannedTargetKeys = plan.Changes
            .Select(change => $"{change.Target.IdentityKey}|{change.Locator.EffectiveResolutionKey.SpanStart}|{change.Locator.EffectiveResolutionKey.SpanLength}")
            .ToHashSet(StringComparer.Ordinal);
        var covered = decisions
            .Where(decision => !plannedTargetKeys.Contains(decision.TargetKey))
            .Where(decision => decision.Target.TargetKind is ModelPrimitives.TargetKind.Method or ModelPrimitives.TargetKind.Statement)
            .Where(decision => classDeleteIds.Any(classId => IsCoveredByClassDelete(decision.Target, classId)))
            .GroupBy(decision => decision.TargetKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        return new ApplicationAbstractions.PlanCoverageSummary(
            covered.Count(decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Method),
            covered.Count(decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Statement),
            covered.Select(decision => decision.Locator.DisplayText)
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray());
    }

    /// <summary>
    /// 构建函数影响摘要。
    /// </summary>
    private static ModelPlanning.FunctionImpactSummary? BuildFunctionImpactSummary(ModelPlanning.FunctionImpactSet? impactSet)
    {
        if (impactSet == null || impactSet.DeletedFunctionIds.Count == 0)
        {
            return null;
        }

        return new ModelPlanning.FunctionImpactSummary(
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
    private static ApplicationAbstractions.ReferenceZeroPredictionSummary BuildReferenceZeroPredictionSummary(
        IReadOnlyList<ModelRules.MarkDecision> decisions)
    {
        var predictedMethods = decisions
            .Where(decision => decision.Reason.Origin == ModelPrimitives.DecisionOrigin.Prediction)
            .Select(decision => decision.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new ApplicationAbstractions.ReferenceZeroPredictionSummary(
            predictedMethods.Length,
            predictedMethods.Take(5).ToArray());
    }

    /// <summary>
    /// 构建边界提升摘要。
    /// </summary>
    private static ApplicationAbstractions.BoundaryPromotionSummary BuildBoundaryPromotionSummary(
        IReadOnlyList<ModelRules.MarkDecision> decisions)
    {
        var promotedMethods = decisions
            .Where(decision => decision.Reason.Origin == ModelPrimitives.DecisionOrigin.BoundaryPromotion)
            .Select(decision => decision.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new ApplicationAbstractions.BoundaryPromotionSummary(
            ModelPrimitives.BoundaryKind.Invocation,
            promotedMethods.Length,
            promotedMethods.Take(5).ToArray());
    }

    /// <summary>
    /// 判断目标是否被类删除覆盖。
    /// </summary>
    private static bool IsCoveredByClassDelete(ModelPrimitives.TargetIdentity target, string classDeleteId)
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
