namespace TerrariaTools.Dome.Application;

using System.Collections.Concurrent;
using System.Diagnostics;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using TerrariaTools.Dome.Rules;

public interface IDomeApplicationRunner
{
    Task<RunResult> RunAsync(RunRequest request, CancellationToken cancellationToken);
}

public sealed class DomeApplication : IDomeApplicationRunner
{
    private readonly IWorkspaceLoader _workspaceLoader;
    private readonly IAnalysisEngine _analysisEngine;
    private readonly IFunctionImpactAnalyzer _functionImpactAnalyzer;
    private readonly IReferenceZeroPredictionAnalyzer _referenceZeroPredictionAnalyzer;
    private readonly MarkingRuleEngine _markingRuleEngine;
    private readonly IRewriteExecutor _rewriteExecutor;
    private readonly RunReportBuilder _runReportBuilder;
    private readonly ArtifactPlanBuilder _artifactPlanBuilder;
    private readonly IRewriteOutputStore _rewriteOutputStore;
    private readonly IArtifactEmissionService _artifactEmissionService;
    private readonly IDomeProgressReporter _progressReporter;

    public DomeApplication(
        IWorkspaceLoader workspaceLoader,
        IAnalysisEngine analysisEngine,
        IFunctionImpactAnalyzer functionImpactAnalyzer,
        IReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer,
        MarkingRuleEngine markingRuleEngine,
        IRewriteExecutor rewriteExecutor,
        RunReportBuilder runReportBuilder,
        ArtifactPlanBuilder artifactPlanBuilder,
        IArtifactWriter artifactWriter,
        IRewriteOutputStore? rewriteOutputStore = null,
        IArtifactEmissionService? artifactEmissionService = null,
        IDomeProgressReporter? progressReporter = null)
    {
        _workspaceLoader = workspaceLoader;
        _analysisEngine = analysisEngine;
        _functionImpactAnalyzer = functionImpactAnalyzer;
        _referenceZeroPredictionAnalyzer = referenceZeroPredictionAnalyzer;
        _markingRuleEngine = markingRuleEngine;
        _rewriteExecutor = rewriteExecutor;
        _runReportBuilder = runReportBuilder;
        _artifactPlanBuilder = artifactPlanBuilder;
        _rewriteOutputStore = rewriteOutputStore ?? new FileSystemRewriteOutputStore();
        _artifactEmissionService = artifactEmissionService ?? new ArtifactEmissionService(artifactWriter);
        _progressReporter = progressReporter ?? NullDomeProgressReporter.Instance;
    }

    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var runStopwatch = Stopwatch.StartNew();
        _progressReporter.Report($"[dome] Starting workspace load: {request.InputPath}");
        var loadResult = await _workspaceLoader.LoadAsync(request.InputPath, request.WorkspaceLoadOptions, cancellationToken);
        _progressReporter.Report($"[dome] Workspace load completed with {loadResult.Documents.Count} C# documents in {FormatElapsed(runStopwatch.Elapsed)}.");
        if (!loadResult.IsSuccess || loadResult.Documents.Count == 0)
        {
            var message = loadResult.Diagnostics.FirstOrDefault()?.Message ?? "No C# input files were found.";
            var artifactPlan = _artifactPlanBuilder.BuildWorkspaceLoadFailure();
            var loadFailureReport = _runReportBuilder.BuildWorkspaceLoadFailure(loadResult, message, artifactPlan.GeneratedArtifacts);
            await _artifactEmissionService.EmitAsync(request.OutputPath, artifactPlan, null, loadFailureReport, null, cancellationToken);
            return RunResult.Failure(FailureCode.WorkspaceLoadFailed, request.OutputPath, message);
        }

        AnalysisEngineResult analysisResult;
        try
        {
            analysisResult = await AnalyzeWorkspaceAsync(loadResult, request, cancellationToken);
        }
        catch (Exception ex)
        {
            var artifactPlan = _artifactPlanBuilder.BuildAnalysisFailure();
            var report = _runReportBuilder.BuildAnalysisFailure(loadResult, ex.Message, artifactPlan.GeneratedArtifacts);
            await _artifactEmissionService.EmitAsync(request.OutputPath, artifactPlan, null, report, null, cancellationToken);
            return RunResult.Failure(FailureCode.AnalysisFailed, request.OutputPath, ex.Message);
        }

        var analyzedWorkspace = CreateAnalyzedWorkspace(analysisResult);
        FunctionImpactSet? functionImpactSet = null;

        if (request.Mode == RunMode.AnalyzeOnly)
        {
            var artifactPlan = _artifactPlanBuilder.BuildAnalyzeOnlySuccess();
            var analyzeReport = _runReportBuilder.BuildAnalyzeOnlySuccess(
                analysisResult.View,
                loadResult,
                artifactPlan.GeneratedArtifacts,
                analyzedWorkspace.AdvancedAnalysisSummary);
            await _artifactEmissionService.EmitAsync(request.OutputPath, artifactPlan, null, analyzeReport, analysisResult.View, cancellationToken);
            return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
        }

        var decisionSet = BuildMarkingDecisions(analyzedWorkspace, cancellationToken);
        var planResult = CompilePlan(request, decisionSet.AllDecisions);

        if (planResult.Plan != null)
        {
            functionImpactSet = _functionImpactAnalyzer.Analyze(
                planResult.Plan,
                analyzedWorkspace.Context.Services,
                FunctionGraphRequests.WholeProjectCalls("DomeApplication", "Whole-project impact summary"));
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
                decisionSet.InitialDecisions,
                decisionSet.PredictedDecisions,
                artifactPlan.GeneratedArtifacts,
                analyzedWorkspace.AdvancedAnalysisSummary);
            await _artifactEmissionService.EmitAsync(request.OutputPath, artifactPlan, null, report, null, cancellationToken);
            return RunResult.Failure(FailureCode.PlanCompileFailed, request.OutputPath, planResult.Message);
        }

        if (request.Mode == RunMode.PlanOnly)
        {
            var artifactPlan = _artifactPlanBuilder.BuildPlanOnlySuccess();
            var planReport = _runReportBuilder.BuildPlanOnlySuccess(
                analysisResult.View,
                loadResult,
                decisionSet.AllDecisions,
                planResult.Plan,
                functionImpactSet,
                artifactPlan.GeneratedArtifacts,
                analyzedWorkspace.AdvancedAnalysisSummary);
            await _artifactEmissionService.EmitAsync(request.OutputPath, artifactPlan, planResult.Plan, planReport, null, cancellationToken);
            return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
        }

        var rewriteOutcome = await RewriteDocumentsAsync(request, analysisResult, planResult.Plan, cancellationToken);
        if (rewriteOutcome.FailureMessage != null)
        {
            var artifactPlan = _artifactPlanBuilder.BuildRewriteFailure(rewriteOutcome.RewrittenDocuments);
            var report = _runReportBuilder.BuildRewriteFailure(
                analysisResult.View,
                loadResult,
                planResult.Plan,
                rewriteOutcome.RewrittenDocuments.Count,
                new PlanCoverageSummary(0, 0, Array.Empty<string>()),
                functionImpactSet,
                decisionSet.InitialDecisions,
                decisionSet.PredictedDecisions,
                rewriteOutcome.FailureMessage,
                artifactPlan.GeneratedArtifacts,
                analyzedWorkspace.AdvancedAnalysisSummary);
            await _artifactEmissionService.EmitAsync(request.OutputPath, artifactPlan, planResult.Plan, report, null, cancellationToken);
            return RunResult.Failure(FailureCode.RewriteFailed, request.OutputPath, rewriteOutcome.FailureMessage);
        }

        var successArtifactPlan = _artifactPlanBuilder.BuildStandardSuccess(rewriteOutcome.RewrittenDocuments);
        var finalReport = _runReportBuilder.BuildStandardSuccess(
            analysisResult.View,
            loadResult,
            decisionSet.AllDecisions,
            planResult.Plan,
            rewriteOutcome.RewrittenDocuments.Count,
            functionImpactSet,
            successArtifactPlan.GeneratedArtifacts,
            analyzedWorkspace.AdvancedAnalysisSummary);
        await _artifactEmissionService.EmitAsync(request.OutputPath, successArtifactPlan, planResult.Plan, finalReport, null, cancellationToken);
        _progressReporter.Report($"[dome] Run completed with {rewriteOutcome.RewrittenDocuments.Count} rewritten documents in {FormatElapsed(runStopwatch.Elapsed)}.");
        return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
    }

    private async Task<AnalysisEngineResult> AnalyzeWorkspaceAsync(
        WorkspaceLoadResult loadResult,
        RunRequest request,
        CancellationToken cancellationToken)
    {
        var analysisStopwatch = Stopwatch.StartNew();
        _progressReporter.Report("[dome] Starting Roslyn analysis...");
        var analysisResult = await _analysisEngine.AnalyzeAsync(
            loadResult.AnalysisInput ?? new SourceOnlyAnalysisInput(request.InputPath, loadResult.Documents),
            cancellationToken);
        _progressReporter.Report($"[dome] Analysis completed with {analysisResult.View.Targets.Count} targets in {FormatElapsed(analysisStopwatch.Elapsed)}.");
        return analysisResult;
    }

    private static AnalyzedWorkspace CreateAnalyzedWorkspace(AnalysisEngineResult analysisResult)
    {
        var context = analysisResult.CreateContext();
        return new AnalyzedWorkspace(context, context.AdvancedAnalysis.BuildSummary());
    }

    private DecisionSet BuildMarkingDecisions(AnalyzedWorkspace analyzedWorkspace, CancellationToken cancellationToken)
    {
        var markingStopwatch = Stopwatch.StartNew();
        _progressReporter.Report("[dome] Building marking decisions...");
        var initialDecisions = _markingRuleEngine.Execute(
            analyzedWorkspace.Context.Snapshot,
            analyzedWorkspace.Context.Services,
            new RuleExecutionContext(
                "DomeApplication",
                null,
                StatementScopeMode.MinimalBlock,
                cancellationToken,
                "Primary marking pass"));
        var predictedDecisions = _referenceZeroPredictionAnalyzer.Predict(
            analyzedWorkspace.Context.Snapshot,
            analyzedWorkspace.Context.Services,
            new RuleExecutionContext(
                "DomeApplication",
                null,
                StatementScopeMode.MinimalBlock,
                cancellationToken,
                "Reference-zero prediction pass"),
            initialDecisions);
        var allDecisions = initialDecisions.Concat(predictedDecisions).ToArray();
        _progressReporter.Report($"[dome] Built {initialDecisions.Count} initial and {predictedDecisions.Count} predicted decisions in {FormatElapsed(markingStopwatch.Elapsed)}.");
        return new DecisionSet(initialDecisions, predictedDecisions, allDecisions);
    }

    private PlanCompilationResult CompilePlan(RunRequest request, IReadOnlyList<MarkDecision> decisions)
    {
        var planStopwatch = Stopwatch.StartNew();
        _progressReporter.Report("[dome] Compiling audit plan...");
        var planResult = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", request.InputPath, request.OutputPath, request.Mode),
            decisions);
        _progressReporter.Report($"[dome] Audit plan compiled: success={planResult.IsSuccess}, changes={(planResult.Plan?.Changes.Count ?? 0)}, conflicts={planResult.Conflicts.Count}, elapsed={FormatElapsed(planStopwatch.Elapsed)}.");
        return planResult;
    }

    private async Task<RewriteOutcome> RewriteDocumentsAsync(
        RunRequest request,
        AnalysisEngineResult analysisResult,
        AuditPlan plan,
        CancellationToken cancellationToken)
    {
        var rewriteStopwatch = Stopwatch.StartNew();
        _progressReporter.Report($"[dome] Starting rewrite for {analysisResult.Documents.Count} documents...");
        var rewrittenDocuments = new ConcurrentBag<string>();
        var rewriteInputs = analysisResult.Documents
            .Select(document => new DocumentRewriteInput(
                document,
                new AuditPlan(
                    plan.Metadata,
                    plan.Changes.Where(change => change.Target.DocumentPath == document.Document.RelativePath).ToArray(),
                    plan.Conflicts)))
            .ToArray();

        var completedCount = 0;
        string? rewriteFailureMessage = null;
        await Parallel.ForEachAsync(
            rewriteInputs,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            },
            async (rewriteInput, token) =>
            {
                var rewriteContext = new RewriteExecutionDocumentContext(
                    rewriteInput.Document.Document,
                    rewriteInput.Document.Root,
                    rewriteInput.Document.SemanticModel);
                var rewriteResult = await _rewriteExecutor.ExecuteAsync(rewriteContext, rewriteInput.Plan, token);
                if (!rewriteResult.IsSuccess || rewriteResult.RewrittenSource == null)
                {
                    Interlocked.CompareExchange(
                        ref rewriteFailureMessage,
                        rewriteResult.Message ?? $"Rewrite failed for '{rewriteInput.Document.Document.RelativePath}'.",
                        null);
                    return;
                }

                try
                {
                    await _rewriteOutputStore.SaveAsync(request.OutputPath, rewriteInput.Document.Document.RelativePath, rewriteResult.RewrittenSource, token);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(
                        ref rewriteFailureMessage,
                        ex.Message,
                        null);
                    return;
                }

                rewrittenDocuments.Add(Path.Combine("rewritten", rewriteInput.Document.Document.RelativePath));

                var completed = Interlocked.Increment(ref completedCount);
                if (completed == rewriteInputs.Length || completed % 100 == 0)
                {
                    _progressReporter.Report($"[dome] Rewrite progress {completed}/{rewriteInputs.Length} after {FormatElapsed(rewriteStopwatch.Elapsed)}.");
                }
            });

        return new RewriteOutcome(
            rewrittenDocuments.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            rewriteFailureMessage);
    }

    private sealed record DocumentRewriteInput(
        AnalysisDocumentContext Document,
        AuditPlan Plan);

    private sealed record AnalyzedWorkspace(
        AnalysisContext Context,
        AdvancedAnalysisSummary AdvancedAnalysisSummary);

    private sealed record DecisionSet(
        IReadOnlyList<MarkDecision> InitialDecisions,
        IReadOnlyList<MarkDecision> PredictedDecisions,
        IReadOnlyList<MarkDecision> AllDecisions);

    private sealed record RewriteOutcome(
        IReadOnlyList<string> RewrittenDocuments,
        string? FailureMessage);

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1} s"
            : $"{elapsed.TotalMilliseconds:F0} ms";

}
