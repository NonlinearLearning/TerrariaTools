namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

/// <summary>
/// Dome 应用程序核心类，负责协调分析、规划和重写流程。
/// </summary>
public sealed class DomeApplication
{
    private readonly SourceWorkspaceLoader _workspaceLoader;
    private readonly RoslynAnalysisEngine _analysisEngine;
    private readonly MarkingRuleEngine _markingRuleEngine;
    private readonly RoslynRewriteExecutor _rewriteExecutor;
    private readonly JsonArtifactWriter _artifactWriter;

    /// <summary>
    /// 初始化 DomeApplication 的新实例。
    /// </summary>
    /// <param name="workspaceLoader">源代码工作区加载器。</param>
    /// <param name="analysisEngine">Roslyn 分析引擎。</param>
    /// <param name="markingRuleEngine">标记规则引擎。</param>
    /// <param name="rewriteExecutor">Roslyn 重写执行器。</param>
    /// <param name="artifactWriter">JSON 制品写入器。</param>
    public DomeApplication(
        SourceWorkspaceLoader workspaceLoader,
        RoslynAnalysisEngine analysisEngine,
        MarkingRuleEngine markingRuleEngine,
        RoslynRewriteExecutor rewriteExecutor,
        JsonArtifactWriter artifactWriter)
    {
        _workspaceLoader = workspaceLoader;
        _analysisEngine = analysisEngine;
        _markingRuleEngine = markingRuleEngine;
        _rewriteExecutor = rewriteExecutor;
        _artifactWriter = artifactWriter;
    }

    /// <summary>
    /// 异步运行应用程序。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果。</returns>
    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var documents = await _workspaceLoader.LoadAsync(request.InputPath, cancellationToken);
        if (documents.Count == 0)
        {
            return RunResult.Failure(FailureCode.WorkspaceLoadFailed, request.OutputPath, "No C# input files were found.");
        }

        RoslynAnalysisResult analysisResult;
        try
        {
            analysisResult = await _analysisEngine.AnalyzeAsync(documents, cancellationToken);
        }
        catch (Exception ex)
        {
            return RunResult.Failure(FailureCode.AnalysisFailed, request.OutputPath, ex.Message);
        }

        var analysisContext = _analysisEngine.CreateContext(analysisResult);
        var generatedArtifacts = new List<string>();
        var riskSummary = BuildRiskSummary(analysisResult.View);
        var emptyCoverageSummary = new PlanCoverageSummary(0, 0, Array.Empty<string>());

        if (request.Mode == RunMode.AnalyzeOnly)
        {
            var analysisPath = Path.Combine(request.OutputPath, "analysis.json");
            await _artifactWriter.WriteAnalysisAsync(analysisPath, analysisResult.View, cancellationToken);
            generatedArtifacts.Add("analysis.json");
            generatedArtifacts.Add("report.json");

            var analyzeReport = new RunReport(
                true,
                FailureCode.None,
                analysisResult.View.Targets.Count,
                0,
                0,
                0,
                generatedArtifacts,
                null,
                Array.Empty<ConflictSummary>(),
                riskSummary,
                emptyCoverageSummary,
                null);
            await WriteArtifactsAsync(request.OutputPath, null, analyzeReport, null, cancellationToken);
            return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
        }

        var decisions = _markingRuleEngine.Execute(analysisContext);
        var planResult = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", request.InputPath, request.OutputPath, request.Mode),
            decisions);
        var coverageSummary = planResult.Plan == null
            ? emptyCoverageSummary
            : BuildPlanCoverageSummary(decisions, planResult.Plan);

        if (!planResult.IsSuccess || planResult.Plan == null)
        {
            var conflictSummaries = BuildConflictSummaries(planResult.Conflicts);
            await WriteArtifactsAsync(
                request.OutputPath,
                null,
                new RunReport(
                    false,
                    FailureCode.PlanCompileFailed,
                    analysisResult.View.Targets.Count,
                    0,
                    planResult.Conflicts.Count,
                    0,
                    new[] { "report.json" },
                    new FailureSummary(FailureCode.PlanCompileFailed, planResult.Message ?? "Plan compilation failed."),
                    conflictSummaries,
                    riskSummary,
                    coverageSummary,
                    planResult.Message),
                null,
                cancellationToken);

            return RunResult.Failure(FailureCode.PlanCompileFailed, request.OutputPath, planResult.Message);
        }

        generatedArtifacts.Add("audit-plan.json");

        if (request.Mode == RunMode.PlanOnly)
        {
            generatedArtifacts.Add("report.json");
            var planReport = new RunReport(
                true,
                FailureCode.None,
                analysisResult.View.Targets.Count,
                planResult.Plan.Changes.Count,
                planResult.Plan.Conflicts.Count,
                0,
                generatedArtifacts,
                null,
                BuildConflictSummaries(planResult.Plan.Conflicts),
                riskSummary,
                coverageSummary,
                null);
            await WriteArtifactsAsync(request.OutputPath, planResult.Plan, planReport, null, cancellationToken);
            return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
        }

        var rewrittenDocuments = new List<string>();
        foreach (var document in analysisResult.Documents)
        {
            var documentPlan = new AuditPlan(
                planResult.Plan.Metadata,
                planResult.Plan.Changes.Where(change => change.Target.DocumentPath == document.Document.RelativePath).ToArray(),
                planResult.Plan.Conflicts);

            var rewriteResult = await _rewriteExecutor.ExecuteAsync(document.Document.SourceText, documentPlan, cancellationToken);
            if (!rewriteResult.IsSuccess || rewriteResult.RewrittenSource == null)
            {
                await WriteArtifactsAsync(
                    request.OutputPath,
                    planResult.Plan,
                    new RunReport(
                        false,
                        FailureCode.RewriteFailed,
                        analysisResult.View.Targets.Count,
                        documentPlan.Changes.Count,
                        documentPlan.Conflicts.Count,
                        rewrittenDocuments.Count,
                        generatedArtifacts.Append("report.json").ToArray(),
                        new FailureSummary(FailureCode.RewriteFailed, rewriteResult.Message ?? "Rewrite failed."),
                        BuildConflictSummaries(documentPlan.Conflicts),
                        riskSummary,
                        coverageSummary,
                        rewriteResult.Message),
                    null,
                    cancellationToken);

                return RunResult.Failure(FailureCode.RewriteFailed, request.OutputPath, rewriteResult.Message);
            }

            var outputPath = Path.Combine(request.OutputPath, "rewritten", document.Document.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, rewriteResult.RewrittenSource, cancellationToken);
            rewrittenDocuments.Add(Path.Combine("rewritten", document.Document.RelativePath));
        }

        generatedArtifacts.AddRange(rewrittenDocuments);
        generatedArtifacts.Add("report.json");
        var finalReport = new RunReport(
            true,
            FailureCode.None,
            analysisResult.View.Targets.Count,
            planResult.Plan.Changes.Count,
            0,
            rewrittenDocuments.Count,
            generatedArtifacts,
            null,
            BuildConflictSummaries(planResult.Plan.Conflicts),
            riskSummary,
            coverageSummary,
            null);
        await WriteArtifactsAsync(request.OutputPath, planResult.Plan, finalReport, null, cancellationToken);
        return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
    }

    /// <summary>
    /// 构建冲突摘要列表。
    /// </summary>
    /// <param name="conflicts">计划冲突列表。</param>
    /// <returns>冲突摘要列表。</returns>
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
    /// <param name="view">分析视图。</param>
    /// <returns>风险摘要。</returns>
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

    /// <summary>
    /// 构建计划覆盖率摘要。
    /// </summary>
    /// <param name="decisions">标记决策列表。</param>
    /// <param name="plan">审计计划。</param>
    /// <returns>计划覆盖率摘要。</returns>
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

    /// <summary>
    /// 检查目标是否被类删除操作覆盖。
    /// </summary>
    /// <param name="target">计划目标。</param>
    /// <param name="classDeleteId">类删除 ID。</param>
    /// <returns>如果被覆盖则返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 异步写入制品。
    /// </summary>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="report">运行报告。</param>
    /// <param name="analysisView">分析视图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WriteArtifactsAsync(
        string outputPath,
        AuditPlan? plan,
        RunReport report,
        AnalysisView? analysisView,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputPath);

        if (analysisView != null)
        {
            await _artifactWriter.WriteAnalysisAsync(Path.Combine(outputPath, "analysis.json"), analysisView, cancellationToken);
        }

        if (plan != null)
        {
            await _artifactWriter.WritePlanAsync(Path.Combine(outputPath, "audit-plan.json"), plan, cancellationToken);
        }

        await _artifactWriter.WriteReportAsync(Path.Combine(outputPath, "report.json"), report, cancellationToken);
    }
}
