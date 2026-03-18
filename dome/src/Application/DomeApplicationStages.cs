namespace TerrariaTools.Dome.Application;

using System.Collections.Concurrent;
using System.Diagnostics;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Rules;

internal sealed class WorkspaceLoadStage(
    ApplicationAbstractions.IWorkspaceLoader workspaceLoader,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        progressReporter.Report($"[dome] Starting workspace load: {context.Request.InputPath}");
        var loadResult = await workspaceLoader.LoadAsync(context.Request.InputPath, context.Request.WorkspaceLoadOptions, cancellationToken);
        context.SetLoadResult(loadResult);
        progressReporter.Report($"[dome] Workspace load completed with {loadResult.Documents.Count} C# documents in {DomeStageFormatting.FormatElapsed(context.RunStopwatch.Elapsed)}.");
        if (loadResult.IsSuccess && loadResult.Documents.Count > 0)
        {
            return;
        }

        var message = loadResult.Diagnostics.FirstOrDefault()?.Message ?? "No C# input files were found.";
        var artifactPlan = artifactPlanBuilder.BuildWorkspaceLoadFailure();
        var report = runReportBuilder.BuildWorkspaceLoadFailure(loadResult, message, artifactPlan.GeneratedArtifacts);
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, null, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteFailure(context, ModelPrimitives.FailureCode.WorkspaceLoadFailed, context.Request.OutputPath, message);
    }
}

internal sealed class AnalysisStage(
    ApplicationAbstractions.IAnalysisEngine analysisEngine,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before analysis.");
        var analysisStopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Starting Roslyn analysis...");
        try
        {
            var sourceSet = loadResult.SourceSet ?? throw new InvalidOperationException("Successful workspace load must include a source set.");
            var analysisResult = await analysisEngine.AnalyzeAsync(sourceSet, cancellationToken);
            context.SetAnalysisResult(
                analysisResult,
                new DomeAnalyzedWorkspace(analysisResult.CreateContext(), analysisResult.Services.AdvancedAnalysis.BuildSummary()));
            progressReporter.Report($"[dome] Analysis completed with {analysisResult.View.Targets.Count} targets in {DomeStageFormatting.FormatElapsed(analysisStopwatch.Elapsed)}.");
        }
        catch (Exception ex)
        {
            var artifactPlan = artifactPlanBuilder.BuildAnalysisFailure();
            var report = runReportBuilder.BuildAnalysisFailure(loadResult, ex.Message, artifactPlan.GeneratedArtifacts);
            await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, null, report, null, cancellationToken);
            DomeTerminalCompletion.CompleteFailure(context, ModelPrimitives.FailureCode.AnalysisFailed, context.Request.OutputPath, ex.Message);
        }
    }
}

internal sealed class AnalyzeOnlyFinalizeStage(
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService) : PipelineStage<DomePipelineContext>
{
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Mode != ModelPrimitives.RunMode.AnalyzeOnly)
        {
            return;
        }

        var analysisResult = context.AnalysisResult ?? throw new InvalidOperationException("Analysis result is required for analyze-only finalize.");
        var analyzedWorkspace = context.AnalyzedWorkspace ?? throw new InvalidOperationException("Analyzed workspace is required for analyze-only finalize.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required for analyze-only finalize.");
        var artifactPlan = artifactPlanBuilder.BuildAnalyzeOnlySuccess();
        var report = runReportBuilder.BuildAnalyzeOnlySuccess(
            analysisResult.View,
            loadResult,
            artifactPlan.GeneratedArtifacts,
            analyzedWorkspace.AdvancedAnalysisSummary);
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, null, report, analysisResult.View, cancellationToken);
        DomeTerminalCompletion.CompleteSuccess(context, context.Request.OutputPath);
    }
}

internal sealed class MarkDecisionsStage(
    IMarkDecisionBuilder markDecisionBuilder,
    ApplicationAbstractions.IReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var analyzedWorkspace = context.AnalyzedWorkspace ?? throw new InvalidOperationException("Analyzed workspace is required before marking decisions.");
        var stopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Building marking decisions...");
        var initialDecisions = markDecisionBuilder.BuildDecisions(analyzedWorkspace.Context, cancellationToken);
        var predictedDecisions = referenceZeroPredictionAnalyzer.Predict(
            analyzedWorkspace.Context,
            initialDecisions);
        var allDecisions = initialDecisions.Concat(predictedDecisions).ToArray();
        context.SetDecisions(new DomeDecisionSet(initialDecisions, predictedDecisions, allDecisions));
        progressReporter.Report($"[dome] Built {initialDecisions.Count} initial and {predictedDecisions.Count} predicted decisions in {DomeStageFormatting.FormatElapsed(stopwatch.Elapsed)}.");
        return Task.CompletedTask;
    }
}

internal sealed class CompilePlanStage(
    ApplicationAbstractions.IFunctionImpactAnalyzer functionImpactAnalyzer,
    ArtifactPlanBuilder artifactPlanBuilder,
    RunReportBuilder runReportBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required before plan compilation.");
        var analyzedWorkspace = context.AnalyzedWorkspace ?? throw new InvalidOperationException("Analyzed workspace is required before plan compilation.");
        var analysisResult = context.AnalysisResult ?? throw new InvalidOperationException("Analysis result is required before plan compilation.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before plan compilation.");

        var stopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Compiling audit plan...");
        var planResult = ModelPlanning.AuditPlanCompiler.Compile(
            new ModelPlanning.PlanMetadata("dome", "1", context.Request.InputPath, context.Request.OutputPath, context.Request.Mode),
            decisions.AllDecisions);
        context.SetPlanResult(planResult);
        progressReporter.Report($"[dome] Audit plan compiled: success={planResult.IsSuccess}, changes={(planResult.Plan?.Changes.Count ?? 0)}, conflicts={planResult.Conflicts.Count}, elapsed={DomeStageFormatting.FormatElapsed(stopwatch.Elapsed)}.");

        if (planResult.Plan != null)
        {
            context.SetFunctionImpactSet(functionImpactAnalyzer.Analyze(
                planResult.Plan,
                analyzedWorkspace.Context.Services,
                ModelAnalysis.FunctionGraphRequests.WholeProjectCalls("DomeApplication", "Whole-project impact summary")));
        }

        if (planResult.IsSuccess && planResult.Plan != null)
        {
            return;
        }

        var artifactPlan = artifactPlanBuilder.BuildPlanCompileFailure();
        var report = runReportBuilder.BuildPlanCompileFailure(
            analysisResult.View,
            loadResult,
            planResult,
            new ApplicationAbstractions.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            context.FunctionImpactSet,
            decisions.InitialDecisions,
            decisions.PredictedDecisions,
            artifactPlan.GeneratedArtifacts,
            analyzedWorkspace.AdvancedAnalysisSummary);
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, null, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteFailure(context, ModelPrimitives.FailureCode.PlanCompileFailed, context.Request.OutputPath, planResult.Message);
    }
}

internal sealed class PlanOnlyFinalizeStage(
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService) : PipelineStage<DomePipelineContext>
{
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Mode != ModelPrimitives.RunMode.PlanOnly)
        {
            return;
        }

        var analysisResult = context.AnalysisResult ?? throw new InvalidOperationException("Analysis result is required for plan-only finalize.");
        var analyzedWorkspace = context.AnalyzedWorkspace ?? throw new InvalidOperationException("Analyzed workspace is required for plan-only finalize.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required for plan-only finalize.");
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required for plan-only finalize.");
        var plan = context.PlanResult?.Plan ?? throw new InvalidOperationException("Compiled plan is required for plan-only finalize.");
        var artifactPlan = artifactPlanBuilder.BuildPlanOnlySuccess();
        var report = runReportBuilder.BuildPlanOnlySuccess(
            analysisResult.View,
            loadResult,
            decisions.AllDecisions,
            plan,
            context.FunctionImpactSet,
            artifactPlan.GeneratedArtifacts,
            analyzedWorkspace.AdvancedAnalysisSummary);
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, plan, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteSuccess(context, context.Request.OutputPath);
    }
}

internal sealed class RewriteStage(
    ApplicationAbstractions.IRewriteExecutor rewriteExecutor,
    IRewriteOutputStore rewriteOutputStore,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Mode != ModelPrimitives.RunMode.Standard)
        {
            return;
        }

        var analysisResult = context.AnalysisResult ?? throw new InvalidOperationException("Analysis result is required before rewrite.");
        var analyzedWorkspace = context.AnalyzedWorkspace ?? throw new InvalidOperationException("Analyzed workspace is required before rewrite.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before rewrite.");
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required before rewrite.");
        var plan = context.PlanResult?.Plan ?? throw new InvalidOperationException("Compiled plan is required before rewrite.");
        var sourceSet = loadResult.SourceSet ?? throw new InvalidOperationException("Successful workspace load must include a source set.");

        var rewriteInputs = DomeRewritePlanProjector.BuildRewriteInputs(sourceSet, plan);
        var rewriteStopwatch = Stopwatch.StartNew();
        progressReporter.Report($"[dome] Starting rewrite for {rewriteInputs.Count} documents...");
        var rewrittenDocuments = new ConcurrentBag<string>();

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
                var rewriteResult = await rewriteExecutor.ExecuteAsync(rewriteInput.SourceSet, rewriteInput.Plan, token);
                if (!rewriteResult.IsSuccess || rewriteResult.RewrittenSource == null)
                {
                    Interlocked.CompareExchange(
                        ref rewriteFailureMessage,
                        rewriteResult.Message ?? $"Rewrite failed for '{rewriteInput.RelativePath}'.",
                        null);
                    return;
                }

                try
                {
                    await rewriteOutputStore.SaveAsync(context.Request.OutputPath, rewriteInput.RelativePath, rewriteResult.RewrittenSource, token);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref rewriteFailureMessage, ex.Message, null);
                    return;
                }

                rewrittenDocuments.Add(Path.Combine("rewritten", rewriteInput.RelativePath));
                var completed = Interlocked.Increment(ref completedCount);
                if (completed == rewriteInputs.Count || completed % 100 == 0)
                {
                    progressReporter.Report($"[dome] Rewrite progress {completed}/{rewriteInputs.Count} after {DomeStageFormatting.FormatElapsed(rewriteStopwatch.Elapsed)}.");
                }
            });

        context.SetRewriteOutcome(new DomeRewriteOutcome(
            rewrittenDocuments.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            rewriteFailureMessage));

        if (rewriteFailureMessage == null)
        {
            return;
        }

        var artifactPlan = artifactPlanBuilder.BuildRewriteFailure(context.RewriteOutcome.RewrittenDocuments);
        var report = runReportBuilder.BuildRewriteFailure(
            analysisResult.View,
            loadResult,
            plan,
            context.RewriteOutcome.RewrittenDocuments.Count,
            new ApplicationAbstractions.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            context.FunctionImpactSet,
            decisions.InitialDecisions,
            decisions.PredictedDecisions,
            rewriteFailureMessage,
            artifactPlan.GeneratedArtifacts,
            analyzedWorkspace.AdvancedAnalysisSummary);
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, plan, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteFailure(context, ModelPrimitives.FailureCode.RewriteFailed, context.Request.OutputPath, rewriteFailureMessage);
    }
}

internal sealed class StandardFinalizeStage(
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Mode != ModelPrimitives.RunMode.Standard)
        {
            return;
        }

        var analysisResult = context.AnalysisResult ?? throw new InvalidOperationException("Analysis result is required for standard finalize.");
        var analyzedWorkspace = context.AnalyzedWorkspace ?? throw new InvalidOperationException("Analyzed workspace is required for standard finalize.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required for standard finalize.");
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required for standard finalize.");
        var plan = context.PlanResult?.Plan ?? throw new InvalidOperationException("Compiled plan is required for standard finalize.");
        var rewriteOutcome = context.RewriteOutcome ?? throw new InvalidOperationException("Rewrite outcome is required for standard finalize.");

        var artifactPlan = artifactPlanBuilder.BuildStandardSuccess(rewriteOutcome.RewrittenDocuments);
        var report = runReportBuilder.BuildStandardSuccess(
            analysisResult.View,
            loadResult,
            decisions.AllDecisions,
            plan,
            rewriteOutcome.RewrittenDocuments.Count,
            context.FunctionImpactSet,
            artifactPlan.GeneratedArtifacts,
            analyzedWorkspace.AdvancedAnalysisSummary);
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, plan, report, null, cancellationToken);
        progressReporter.Report($"[dome] Run completed with {rewriteOutcome.RewrittenDocuments.Count} rewritten documents in {DomeStageFormatting.FormatElapsed(context.RunStopwatch.Elapsed)}.");
        DomeTerminalCompletion.CompleteSuccess(context, context.Request.OutputPath);
    }
}

internal sealed record DocumentRewriteInput(
    ApplicationAbstractions.SourceDocumentSet SourceSet,
    string RelativePath,
    ModelPlanning.AuditPlan Plan);

internal static class DomeRewritePlanProjector
{
    internal static IReadOnlyList<DocumentRewriteInput> BuildRewriteInputs(ApplicationAbstractions.SourceDocumentSet sourceSet, ModelPlanning.AuditPlan plan)
    {
        return sourceSet.Documents
            .Select(document => new DocumentRewriteInput(
                new ApplicationAbstractions.SourceDocumentSet(
                    sourceSet.EntryPath,
                    sourceSet.RootPath,
                    [document]),
                document.RelativePath,
                new ModelPlanning.AuditPlan(
                    plan.Metadata,
                    plan.Changes.Where(change => change.Target.DocumentPath == document.RelativePath).ToArray(),
                    plan.Conflicts)))
            .ToArray();
    }
}

internal static class DomeStageFormatting
{
    public static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1} s"
            : $"{elapsed.TotalMilliseconds:F0} ms";
}

internal static class DomeTerminalCompletion
{
    internal static void CompleteSuccess(DomePipelineContext context, string outputPath)
    {
        context.TerminalState = new PipelineTerminalState(
            ApplicationAbstractions.RunResult.Success(outputPath, Path.Combine(outputPath, "report.json")));
    }

    internal static void CompleteFailure(DomePipelineContext context, ModelPrimitives.FailureCode failureCode, string outputPath, string? message)
    {
        context.TerminalState = new PipelineTerminalState(
            ApplicationAbstractions.RunResult.Failure(failureCode, outputPath, message));
    }
}
