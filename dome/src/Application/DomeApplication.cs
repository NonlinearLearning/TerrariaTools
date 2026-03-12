namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

public sealed class DomeApplication
{
    private readonly SourceWorkspaceLoader _workspaceLoader;
    private readonly RoslynAnalysisEngine _analysisEngine;
    private readonly MarkingRuleEngine _markingRuleEngine;
    private readonly RoslynRewriteExecutor _rewriteExecutor;
    private readonly JsonArtifactWriter _artifactWriter;

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

        var generatedArtifacts = new List<string>();
        var riskSummary = BuildRiskSummary(analysisResult.View);

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
                null);
            await WriteArtifactsAsync(request.OutputPath, null, analyzeReport, null, cancellationToken);
            return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
        }

        var decisions = _markingRuleEngine.Execute(analysisResult.View);
        var planResult = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", request.InputPath, request.OutputPath, request.Mode),
            decisions);

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
            null);
        await WriteArtifactsAsync(request.OutputPath, planResult.Plan, finalReport, null, cancellationToken);
        return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
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
