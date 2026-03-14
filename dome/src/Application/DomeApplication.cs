namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

/// <summary>
/// Dome application orchestration entrypoint.
/// </summary>
public sealed class DomeApplication
{
    private readonly IWorkspaceLoader _workspaceLoader;
    private readonly RoslynAnalysisEngine _analysisEngine;
    private readonly FunctionImpactAnalyzer _functionImpactAnalyzer;
    private readonly ReferenceZeroPredictionAnalyzer _referenceZeroPredictionAnalyzer;
    private readonly MarkingRuleEngine _markingRuleEngine;
    private readonly RoslynRewriteExecutor _rewriteExecutor;
    private readonly RunReportBuilder _runReportBuilder;
    private readonly ArtifactPlanBuilder _artifactPlanBuilder;
    private readonly JsonArtifactWriter _artifactWriter;

    public DomeApplication(
        IWorkspaceLoader workspaceLoader,
        RoslynAnalysisEngine analysisEngine,
        FunctionImpactAnalyzer functionImpactAnalyzer,
        ReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer,
        MarkingRuleEngine markingRuleEngine,
        RoslynRewriteExecutor rewriteExecutor,
        RunReportBuilder runReportBuilder,
        ArtifactPlanBuilder artifactPlanBuilder,
        JsonArtifactWriter artifactWriter)
    {
        _workspaceLoader = workspaceLoader;
        _analysisEngine = analysisEngine;
        _functionImpactAnalyzer = functionImpactAnalyzer;
        _referenceZeroPredictionAnalyzer = referenceZeroPredictionAnalyzer;
        _markingRuleEngine = markingRuleEngine;
        _rewriteExecutor = rewriteExecutor;
        _runReportBuilder = runReportBuilder;
        _artifactPlanBuilder = artifactPlanBuilder;
        _artifactWriter = artifactWriter;
    }

    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var loadResult = await _workspaceLoader.LoadAsync(request.InputPath, request.WorkspaceLoadOptions, cancellationToken);
        if (!loadResult.IsSuccess || loadResult.Documents.Count == 0)
        {
            var message = loadResult.Diagnostics.FirstOrDefault()?.Message ?? "No C# input files were found.";
            var artifactPlan = _artifactPlanBuilder.BuildWorkspaceLoadFailure();
            var loadFailureReport = _runReportBuilder.BuildWorkspaceLoadFailure(loadResult, message, artifactPlan.GeneratedArtifacts);
            await WriteArtifactsAsync(request.OutputPath, artifactPlan, null, loadFailureReport, null, cancellationToken);
            return RunResult.Failure(FailureCode.WorkspaceLoadFailed, request.OutputPath, message);
        }

        RoslynAnalysisResult analysisResult;
        try
        {
            analysisResult = await _analysisEngine.AnalyzeAsync(
                loadResult.AnalysisInput ?? new SourceOnlyAnalysisInput(request.InputPath, loadResult.Documents),
                cancellationToken);
        }
        catch (Exception ex)
        {
            var artifactPlan = _artifactPlanBuilder.BuildAnalysisFailure();
            var report = _runReportBuilder.BuildAnalysisFailure(loadResult, ex.Message, artifactPlan.GeneratedArtifacts);
            await WriteArtifactsAsync(request.OutputPath, artifactPlan, null, report, null, cancellationToken);
            return RunResult.Failure(FailureCode.AnalysisFailed, request.OutputPath, ex.Message);
        }

        var analysisContext = _analysisEngine.CreateContext(analysisResult);
        var analysisSnapshot = analysisContext.Snapshot;
        var analysisServices = analysisContext.Services;
        FunctionImpactSet? functionImpactSet = null;

        if (request.Mode == RunMode.AnalyzeOnly)
        {
            var artifactPlan = _artifactPlanBuilder.BuildAnalyzeOnlySuccess();
            var analyzeReport = _runReportBuilder.BuildAnalyzeOnlySuccess(
                analysisResult.View,
                loadResult,
                artifactPlan.GeneratedArtifacts);
            await WriteArtifactsAsync(request.OutputPath, artifactPlan, null, analyzeReport, analysisResult.View, cancellationToken);
            return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
        }

        var initialDecisions = _markingRuleEngine.Execute(
            analysisSnapshot,
            analysisServices,
            new RuleExecutionContext(
                "DomeApplication",
                null,
                StatementScopeMode.MinimalBlock,
                cancellationToken,
                "Primary marking pass"));
        var predictedDecisions = _referenceZeroPredictionAnalyzer.Predict(
            analysisSnapshot,
            analysisServices,
            new RuleExecutionContext(
                "DomeApplication",
                null,
                StatementScopeMode.MinimalBlock,
                cancellationToken,
                "Reference-zero prediction pass"),
            initialDecisions);
        var decisions = initialDecisions
            .Concat(predictedDecisions)
            .ToArray();
        var planResult = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", request.InputPath, request.OutputPath, request.Mode),
            decisions);

        if (planResult.Plan != null)
        {
            functionImpactSet = _functionImpactAnalyzer.Analyze(
                planResult.Plan,
                analysisServices,
                FunctionGraphRequests.WholeProjectCalls(
                    "DomeApplication",
                    "Whole-project impact summary"));
        }

        if (!planResult.IsSuccess || planResult.Plan == null)
        {
            var artifactPlan = _artifactPlanBuilder.BuildPlanCompileFailure();
            var report = _runReportBuilder.BuildPlanCompileFailure(
                analysisResult.View,
                loadResult,
                planResult,
                new PlanCoverageSummary(0, 0, Array.Empty<string>()),
                functionImpactSet,
                initialDecisions,
                predictedDecisions,
                artifactPlan.GeneratedArtifacts);
            await WriteArtifactsAsync(request.OutputPath, artifactPlan, null, report, null, cancellationToken);
            return RunResult.Failure(FailureCode.PlanCompileFailed, request.OutputPath, planResult.Message);
        }

        if (request.Mode == RunMode.PlanOnly)
        {
            var artifactPlan = _artifactPlanBuilder.BuildPlanOnlySuccess();
            var planReport = _runReportBuilder.BuildPlanOnlySuccess(
                analysisResult.View,
                loadResult,
                decisions,
                planResult.Plan,
                functionImpactSet,
                artifactPlan.GeneratedArtifacts);
            await WriteArtifactsAsync(request.OutputPath, artifactPlan, planResult.Plan, planReport, null, cancellationToken);
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
                var artifactPlan = _artifactPlanBuilder.BuildRewriteFailure(rewrittenDocuments);
                var report = _runReportBuilder.BuildRewriteFailure(
                    analysisResult.View,
                    loadResult,
                    documentPlan,
                    rewrittenDocuments.Count,
                    new PlanCoverageSummary(0, 0, Array.Empty<string>()),
                    functionImpactSet,
                    initialDecisions,
                    predictedDecisions,
                    rewriteResult.Message,
                    artifactPlan.GeneratedArtifacts);
                await WriteArtifactsAsync(request.OutputPath, artifactPlan, planResult.Plan, report, null, cancellationToken);
                return RunResult.Failure(FailureCode.RewriteFailed, request.OutputPath, rewriteResult.Message);
            }

            var outputPath = Path.Combine(request.OutputPath, "rewritten", document.Document.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, rewriteResult.RewrittenSource, cancellationToken);
            rewrittenDocuments.Add(Path.Combine("rewritten", document.Document.RelativePath));
        }

        var successArtifactPlan = _artifactPlanBuilder.BuildStandardSuccess(rewrittenDocuments);
        var finalReport = _runReportBuilder.BuildStandardSuccess(
            analysisResult.View,
            loadResult,
            decisions,
            planResult.Plan,
            rewrittenDocuments.Count,
            functionImpactSet,
            successArtifactPlan.GeneratedArtifacts);
        await WriteArtifactsAsync(request.OutputPath, successArtifactPlan, planResult.Plan, finalReport, null, cancellationToken);
        return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
    }

    private async Task WriteArtifactsAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        AuditPlan? plan,
        RunReport report,
        AnalysisView? analysisView,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputPath);

        if (artifactPlan.WriteAnalysis && analysisView != null)
        {
            await _artifactWriter.WriteAnalysisAsync(Path.Combine(outputPath, "analysis.json"), analysisView, cancellationToken);
        }

        if (artifactPlan.WritePlan && plan != null)
        {
            await _artifactWriter.WritePlanAsync(Path.Combine(outputPath, "audit-plan.json"), plan, cancellationToken);
        }

        if (artifactPlan.WriteReport)
        {
            await _artifactWriter.WriteReportAsync(Path.Combine(outputPath, "report.json"), report, cancellationToken);
        }
    }
}
