namespace TerrariaTools.Dome.Application.Pipeline;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 负责将运行过程中的中间结果汇总为统一报告模型。
/// </summary>
public sealed class RunReportBuilder
{
    /// <summary>
    /// 构建工作区加载失败报告。
    /// </summary>
    /// <param name="loadResult">工作区加载结果。</param>
    /// <param name="message">失败消息。</param>
    /// <param name="generatedArtifacts">已生成的产物集合。</param>
    /// <returns>工作区加载失败报告。</returns>
    public ModelExecution.RunReport BuildWorkspaceLoadFailure(
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        string message,
        IReadOnlyList<string> generatedArtifacts)
    {
        return new ModelExecution.RunReport(
            false,
            ModelPrimitives.FailureCode.WorkspaceLoadFailed,
            0,
            0,
            0,
            0,
            generatedArtifacts,
            new ModelExecution.FailureSummary(ModelPrimitives.FailureCode.WorkspaceLoadFailed, message),
            Array.Empty<ModelExecution.ConflictSummary>(),
            new ModelExecution.RiskSummary(0, Array.Empty<string>()),
            new ModelExecution.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            BuildWorkspaceDiagnostics(loadResult.Diagnostics),
            message);
    }

    /// <summary>
    /// 构建分析阶段失败报告。
    /// </summary>
    /// <param name="loadResult">工作区加载结果。</param>
    /// <param name="message">失败消息。</param>
    /// <param name="generatedArtifacts">已生成的产物集合。</param>
    /// <returns>分析失败报告。</returns>
    public ModelExecution.RunReport BuildAnalysisFailure(
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        string message,
        IReadOnlyList<string> generatedArtifacts)
    {
        return new ModelExecution.RunReport(
            false,
            ModelPrimitives.FailureCode.AnalysisFailed,
            0,
            0,
            0,
            0,
            generatedArtifacts,
            new ModelExecution.FailureSummary(ModelPrimitives.FailureCode.AnalysisFailed, message),
            Array.Empty<ModelExecution.ConflictSummary>(),
            new ModelExecution.RiskSummary(0, Array.Empty<string>()),
            new ModelExecution.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            BuildWorkspaceDiagnostics(loadResult.Diagnostics),
            message);
    }

    /// <summary>
    /// 构建仅分析模式成功报告。
    /// </summary>
    /// <param name="view">分析结果视图。</param>
    /// <param name="loadResult">工作区加载结果。</param>
    /// <param name="generatedArtifacts">已生成的产物集合。</param>
    /// <param name="advancedAnalysisSummary">可选的高级分析摘要。</param>
    /// <returns>仅分析成功报告。</returns>
    public ModelExecution.RunReport BuildAnalyzeOnlySuccess(
        CoreAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        IReadOnlyList<string> generatedArtifacts,
        CoreAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ModelExecution.RunReport(
            true,
            ModelPrimitives.FailureCode.None,
            view.Targets.Count,
            0,
            0,
            0,
            generatedArtifacts,
            null,
            Array.Empty<ModelExecution.ConflictSummary>(),
            BuildRiskSummary(view),
            new ModelExecution.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            BuildWorkspaceDiagnostics(loadResult.Diagnostics),
            null)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建计划编译失败报告。
    /// </summary>
    /// <param name="view">分析结果视图。</param>
    /// <param name="loadResult">工作区加载结果。</param>
    /// <param name="planResult">计划编译结果。</param>
    /// <param name="coverageSummary">覆盖率摘要。</param>
    /// <param name="functionImpactSet">可选的函数影响集合。</param>
    /// <param name="initialDecisions">初始决策集合。</param>
    /// <param name="predictedDecisions">预测决策集合。</param>
    /// <param name="generatedArtifacts">已生成的产物集合。</param>
    /// <param name="advancedAnalysisSummary">可选的高级分析摘要。</param>
    /// <returns>计划编译失败报告。</returns>
    public ModelExecution.RunReport BuildPlanCompileFailure(
        CoreAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        CorePlanning.PlanCompilationResult planResult,
        ModelExecution.PlanCoverageSummary coverageSummary,
        CorePlanning.FunctionImpactSet? functionImpactSet,
        IReadOnlyList<CoreRules.MarkDecision> initialDecisions,
        IReadOnlyList<CoreRules.MarkDecision> predictedDecisions,
        IReadOnlyList<string> generatedArtifacts,
        CoreAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ModelExecution.RunReport(
            false,
            ModelPrimitives.FailureCode.PlanCompileFailed,
            view.Targets.Count,
            0,
            planResult.Conflicts.Count,
            0,
            generatedArtifacts,
            new ModelExecution.FailureSummary(ModelPrimitives.FailureCode.PlanCompileFailed, planResult.Message ?? "Plan compilation failed."),
            BuildConflictSummaries(planResult.Conflicts),
            BuildRiskSummary(view),
            coverageSummary,
            BuildFunctionImpactSummary(functionImpactSet),
            BuildBoundaryPromotionSummary(initialDecisions),
            BuildReferenceZeroPredictionSummary(predictedDecisions),
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            BuildWorkspaceDiagnostics(loadResult.Diagnostics),
            planResult.Message)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建仅计划模式成功报告。
    /// </summary>
    /// <param name="view">分析结果视图。</param>
    /// <param name="loadResult">工作区加载结果。</param>
    /// <param name="decisions">最终决策集合。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="functionImpactSet">可选的函数影响集合。</param>
    /// <param name="generatedArtifacts">已生成的产物集合。</param>
    /// <param name="advancedAnalysisSummary">可选的高级分析摘要。</param>
    /// <returns>仅计划成功报告。</returns>
    public ModelExecution.RunReport BuildPlanOnlySuccess(
        CoreAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        IReadOnlyList<CoreRules.MarkDecision> decisions,
        CorePlanning.AuditPlan plan,
        CorePlanning.FunctionImpactSet? functionImpactSet,
        IReadOnlyList<string> generatedArtifacts,
        CoreAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ModelExecution.RunReport(
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
            BuildWorkspaceDiagnostics(loadResult.Diagnostics),
            null)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建重写失败报告。
    /// </summary>
    /// <param name="view">分析结果视图。</param>
    /// <param name="loadResult">工作区加载结果。</param>
    /// <param name="documentPlan">文档级审计计划。</param>
    /// <param name="rewrittenDocumentCount">已完成的重写文档数量。</param>
    /// <param name="coverageSummary">覆盖率摘要。</param>
    /// <param name="functionImpactSet">可选的函数影响集合。</param>
    /// <param name="initialDecisions">初始决策集合。</param>
    /// <param name="predictedDecisions">预测决策集合。</param>
    /// <param name="message">失败消息。</param>
    /// <param name="generatedArtifacts">已生成的产物集合。</param>
    /// <param name="advancedAnalysisSummary">可选的高级分析摘要。</param>
    /// <returns>重写失败报告。</returns>
    public ModelExecution.RunReport BuildRewriteFailure(
        CoreAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        CorePlanning.AuditPlan documentPlan,
        int rewrittenDocumentCount,
        ModelExecution.PlanCoverageSummary coverageSummary,
        CorePlanning.FunctionImpactSet? functionImpactSet,
        IReadOnlyList<CoreRules.MarkDecision> initialDecisions,
        IReadOnlyList<CoreRules.MarkDecision> predictedDecisions,
        string? message,
        IReadOnlyList<string> generatedArtifacts,
        CoreAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ModelExecution.RunReport(
            false,
            ModelPrimitives.FailureCode.RewriteFailed,
            view.Targets.Count,
            documentPlan.Changes.Count,
            documentPlan.Conflicts.Count,
            rewrittenDocumentCount,
            generatedArtifacts,
            new ModelExecution.FailureSummary(ModelPrimitives.FailureCode.RewriteFailed, message ?? "Rewrite failed."),
            BuildConflictSummaries(documentPlan.Conflicts),
            BuildRiskSummary(view),
            coverageSummary,
            BuildFunctionImpactSummary(functionImpactSet),
            BuildBoundaryPromotionSummary(initialDecisions),
            BuildReferenceZeroPredictionSummary(predictedDecisions),
            loadResult.LoadMode,
            loadResult.FallbackUsed,
            BuildWorkspaceDiagnostics(loadResult.Diagnostics),
            message)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 构建标准模式成功报告。
    /// </summary>
    /// <param name="view">分析结果视图。</param>
    /// <param name="loadResult">工作区加载结果。</param>
    /// <param name="decisions">最终决策集合。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="rewrittenDocumentCount">重写文档数量。</param>
    /// <param name="functionImpactSet">可选的函数影响集合。</param>
    /// <param name="generatedArtifacts">已生成的产物集合。</param>
    /// <param name="advancedAnalysisSummary">可选的高级分析摘要。</param>
    /// <returns>标准模式成功报告。</returns>
    public ModelExecution.RunReport BuildStandardSuccess(
        CoreAnalysis.AnalysisResultModel view,
        ApplicationAbstractions.WorkspaceLoadResult loadResult,
        IReadOnlyList<CoreRules.MarkDecision> decisions,
        CorePlanning.AuditPlan plan,
        int rewrittenDocumentCount,
        CorePlanning.FunctionImpactSet? functionImpactSet,
        IReadOnlyList<string> generatedArtifacts,
        CoreAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary = null)
    {
        return new ModelExecution.RunReport(
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
            BuildWorkspaceDiagnostics(loadResult.Diagnostics),
            null)
        {
            AdvancedAnalysisSummary = advancedAnalysisSummary
        };
    }

    /// <summary>
    /// 将工作区加载诊断转换为报告模型。
    /// </summary>
    /// <param name="diagnostics">工作区加载诊断集合。</param>
    /// <returns>报告模型使用的诊断集合。</returns>
    private static IReadOnlyList<ModelExecution.WorkspaceDiagnosticInfo> BuildWorkspaceDiagnostics(
        IReadOnlyList<ApplicationAbstractions.WorkspaceLoadDiagnostic> diagnostics)
    {
        return diagnostics
            .Select(static diagnostic => new ModelExecution.WorkspaceDiagnosticInfo(
                diagnostic.Stage,
                diagnostic.Severity,
                diagnostic.Message))
            .ToArray();
    }

    /// <summary>
    /// 将计划冲突转换为报告摘要。
    /// </summary>
    /// <param name="conflicts">计划冲突集合。</param>
    /// <returns>冲突摘要集合。</returns>
    private static IReadOnlyList<ModelExecution.ConflictSummary> BuildConflictSummaries(IReadOnlyList<CorePlanning.PlanConflict> conflicts)
    {
        return conflicts
            .Select(conflict => new ModelExecution.ConflictSummary(
                conflict.ConflictCode,
                $"{conflict.Target.IdentityKey}|{conflict.Locator.EffectiveResolutionKey.SpanStart}|{conflict.Locator.EffectiveResolutionKey.SpanLength}",
                conflict.Locator.DisplayText,
                conflict.ActionKinds,
                conflict.Reason))
            .ToArray();
    }

    /// <summary>
    /// 统计带指令且被标记为高风险的目标。
    /// </summary>
    /// <param name="view">分析结果视图。</param>
    /// <returns>风险摘要。</returns>
    private static ModelExecution.RiskSummary BuildRiskSummary(CoreAnalysis.AnalysisResultModel view)
    {
        var skippedHighRiskTargets = view.Targets
            .Where(target => target.IsHighRisk && target.Directives.Count > 0)
            .Select(target => target.Locator.DisplayText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ModelExecution.RiskSummary(
            skippedHighRiskTargets.Length,
            skippedHighRiskTargets.Take(5).ToArray());
    }

    /// <summary>
    /// 估算类删除动作间接覆盖的方法和语句数量。
    /// </summary>
    /// <param name="decisions">决策集合。</param>
    /// <param name="plan">审计计划。</param>
    /// <returns>覆盖率摘要。</returns>
    private static ModelExecution.PlanCoverageSummary BuildPlanCoverageSummary(
        IReadOnlyList<CoreRules.MarkDecision> decisions,
        CorePlanning.AuditPlan plan)
    {
        var classDeleteIds = plan.Changes
            .Where(change => change.Target.TargetKind == CoreCommon.TargetKind.Class && change.Action.Kind == CoreCommon.PlanActionKind.Delete)
            .Select(change => $"{change.Target.DocumentPath}|{change.Target.MemberId.Value}")
            .ToArray();
        var plannedTargetKeys = plan.Changes
            .Select(change => $"{change.Target.IdentityKey}|{change.Locator.EffectiveResolutionKey.SpanStart}|{change.Locator.EffectiveResolutionKey.SpanLength}")
            .ToHashSet(StringComparer.Ordinal);
        var covered = decisions
            .Where(decision => !plannedTargetKeys.Contains(decision.TargetKey))
            .Where(decision => decision.Target.TargetKind is CoreCommon.TargetKind.Method or CoreCommon.TargetKind.Statement)
            .Where(decision => classDeleteIds.Any(classId => IsCoveredByClassDelete(decision.Target, classId)))
            .GroupBy(decision => decision.TargetKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        return new ModelExecution.PlanCoverageSummary(
            covered.Count(decision => decision.Target.TargetKind == CoreCommon.TargetKind.Method),
            covered.Count(decision => decision.Target.TargetKind == CoreCommon.TargetKind.Statement),
            covered.Select(decision => decision.Locator.DisplayText)
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray());
    }

    /// <summary>
    /// 将函数影响集合转换为报告摘要。
    /// </summary>
    /// <param name="impactSet">函数影响集合。</param>
    /// <returns>函数影响摘要；如果没有影响则返回 <see langword="null"/>。</returns>
    private static ModelExecution.FunctionImpactSummary? BuildFunctionImpactSummary(CorePlanning.FunctionImpactSet? impactSet)
    {
        if (impactSet == null || impactSet.DeletedFunctionIds.Count == 0)
        {
            return null;
        }

        return new ModelExecution.FunctionImpactSummary(
            impactSet.DeletedFunctionIds.Count,
            impactSet.AffectedFunctionIds.Count,
            impactSet.AffectedDocumentPaths.Count,
            impactSet.ExpansionDepth,
            impactSet.EdgeKinds,
            impactSet.AffectedFunctionIds.Take(5).ToArray(),
            impactSet.AffectedDocumentPaths.Take(5).ToArray());
    }

    /// <summary>
    /// 统计引用归零预测产生的方法删除数量。
    /// </summary>
    /// <param name="decisions">决策集合。</param>
    /// <returns>引用归零预测摘要。</returns>
    private static ModelExecution.ReferenceZeroPredictionSummary BuildReferenceZeroPredictionSummary(
        IReadOnlyList<CoreRules.MarkDecision> decisions)
    {
        var predictedMethods = decisions
            .Where(decision => decision.Reason.Origin == CoreCommon.DecisionOrigin.Prediction)
            .Select(decision => decision.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new ModelExecution.ReferenceZeroPredictionSummary(
            predictedMethods.Length,
            predictedMethods.Take(5).ToArray());
    }

    /// <summary>
    /// 统计边界提升产生的方法删除数量。
    /// </summary>
    /// <param name="decisions">决策集合。</param>
    /// <returns>边界提升摘要。</returns>
    private static ModelExecution.BoundaryPromotionSummary BuildBoundaryPromotionSummary(
        IReadOnlyList<CoreRules.MarkDecision> decisions)
    {
        var promotedMethods = decisions
            .Where(decision => decision.Reason.Origin == CoreCommon.DecisionOrigin.BoundaryPromotion)
            .Select(decision => decision.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new ModelExecution.BoundaryPromotionSummary(
            CoreCommon.BoundaryKind.Invocation,
            promotedMethods.Length,
            promotedMethods.Take(5).ToArray());
    }

    /// <summary>
    /// 判断目标是否被某个类删除动作整体覆盖。
    /// </summary>
    /// <param name="target">待判断的目标。</param>
    /// <param name="classDeleteId">类删除动作标识。</param>
    /// <returns>如果目标属于该类删除范围则返回 <see langword="true"/>。</returns>
    private static bool IsCoveredByClassDelete(CoreCommon.TargetIdentity target, string classDeleteId)
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

