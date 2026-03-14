namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using TerrariaTools.Dome.Rules;

public sealed class RunReportBuilder
{
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

    public RunReport BuildAnalyzeOnlySuccess(
        AnalysisView view,
        WorkspaceLoadResult loadResult,
        IReadOnlyList<string> generatedArtifacts)
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
            null);
    }

    public RunReport BuildPlanCompileFailure(
        AnalysisView view,
        WorkspaceLoadResult loadResult,
        PlanCompilationResult planResult,
        PlanCoverageSummary coverageSummary,
        FunctionImpactSet? functionImpactSet,
        IReadOnlyList<MarkDecision> initialDecisions,
        IReadOnlyList<MarkDecision> predictedDecisions,
        IReadOnlyList<string> generatedArtifacts)
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
            planResult.Message);
    }

    public RunReport BuildPlanOnlySuccess(
        AnalysisView view,
        WorkspaceLoadResult loadResult,
        IReadOnlyList<MarkDecision> decisions,
        AuditPlan plan,
        FunctionImpactSet? functionImpactSet,
        IReadOnlyList<string> generatedArtifacts)
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
            null);
    }

    public RunReport BuildRewriteFailure(
        AnalysisView view,
        WorkspaceLoadResult loadResult,
        AuditPlan documentPlan,
        int rewrittenDocumentCount,
        PlanCoverageSummary coverageSummary,
        FunctionImpactSet? functionImpactSet,
        IReadOnlyList<MarkDecision> initialDecisions,
        IReadOnlyList<MarkDecision> predictedDecisions,
        string? message,
        IReadOnlyList<string> generatedArtifacts)
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
            message);
    }

    public RunReport BuildStandardSuccess(
        AnalysisView view,
        WorkspaceLoadResult loadResult,
        IReadOnlyList<MarkDecision> decisions,
        AuditPlan plan,
        int rewrittenDocumentCount,
        FunctionImpactSet? functionImpactSet,
        IReadOnlyList<string> generatedArtifacts)
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
            null);
    }

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

    private static RiskSummary BuildRiskSummary(AnalysisView view)
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
            .ToArray();

        return new PlanCoverageSummary(
            covered.Count(decision => decision.Target.TargetKind == TargetKind.Method),
            covered.Count(decision => decision.Target.TargetKind == TargetKind.Statement),
            covered.Select(decision => decision.Target.DisplayText)
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray());
    }

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

    private static ReferenceZeroPredictionSummary BuildReferenceZeroPredictionSummary(
        IReadOnlyList<MarkDecision> decisions)
    {
        var predictedMethods = decisions
            .Where(decision => decision.Reason.RuleId == "reference-zero-prediction")
            .Select(decision => decision.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new ReferenceZeroPredictionSummary(
            predictedMethods.Length,
            predictedMethods.Take(5).ToArray());
    }

    private static BoundaryPromotionSummary BuildBoundaryPromotionSummary(
        IReadOnlyList<MarkDecision> decisions)
    {
        var promotedMethods = decisions
            .Where(decision => decision.Reason.RuleId == "boundary-promotion")
            .Select(decision => decision.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new BoundaryPromotionSummary(
            BoundaryKind.Invocation,
            promotedMethods.Length,
            promotedMethods.Take(5).ToArray());
    }

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
