namespace TerrariaTools.Dome.Application.Composition;

using System.Collections.Concurrent;
using System.Diagnostics;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreRules = TerrariaTools.Dome.Core.Rules.Model;
using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Core.Rules.Services;

/// <summary>
/// Groups the active slot implementations for one Dome-family flow.
/// </summary>
internal sealed record DomeFlowSlots(
    ApplicationAbstractions.ILoadSlot Load,
    ApplicationAbstractions.IAnalyzeSlot Analyze,
    ApplicationAbstractions.IRuleSlot Rule,
    ApplicationAbstractions.IDecisionSlot Decision,
    ApplicationAbstractions.IResultSlot Result);

/// <summary>
/// Creates the default Dome-family slot adapters from the current service graph.
/// </summary>
internal static class DomeSlotAdapters
{
    public static DomeFlowSlots CreateDefaults(
        DomePipelineDependencies dependencies,
        IRewriteOutputStore rewriteOutputStore,
        IArtifactEmissionService artifactEmissionService,
        IDomeProgressReporter progressReporter)
    {
        return new DomeFlowSlots(
            new WorkspaceLoadSlotAdapter(dependencies.WorkspaceLoader),
            new AnalysisSlotAdapter(dependencies.AnalysisEngine),
            new RuleSlotAdapter(dependencies.MarkingRuleEngine, dependencies.ReferenceZeroPredictionAnalyzer),
            new DecisionSlotAdapter(dependencies.FunctionImpactAnalyzer),
            new ResultSlotAdapter(
                dependencies.RewriteExecutor,
                rewriteOutputStore,
                dependencies.RunReportBuilder,
                dependencies.ArtifactPlanBuilder,
                artifactEmissionService,
                progressReporter));
    }
}

internal sealed class WorkspaceLoadSlotAdapter(ApplicationAbstractions.IWorkspaceLoader workspaceLoader) : ApplicationAbstractions.ILoadSlot
{
    public async Task<ApplicationAbstractions.LoadOutput> ExecuteAsync(
        ApplicationAbstractions.LoadInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var loadResult = await workspaceLoader.LoadAsync(
            input.Request.InputPath,
            input.Request.WorkspaceLoadOptions,
            cancellationToken);
        return new ApplicationAbstractions.LoadOutput(input.Request, loadResult);
    }
}

internal sealed class AnalysisSlotAdapter(ApplicationAbstractions.IAnalysisEngine analysisEngine) : ApplicationAbstractions.IAnalyzeSlot
{
    public async Task<ApplicationAbstractions.AnalyzeOutput> ExecuteAsync(
        ApplicationAbstractions.AnalyzeInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var analysisInput = input.Load.Workspace.Input ??
            throw new InvalidOperationException("Successful workspace load must include analysis input.");
        var analysisOutput = await analysisEngine.AnalyzeAsync(analysisInput, cancellationToken);
        return new ApplicationAbstractions.AnalyzeOutput(input.Load, analysisOutput);
    }
}

internal sealed class RuleSlotAdapter(
    IMarkDecisionBuilder markDecisionBuilder,
    ApplicationAbstractions.IReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer) : ApplicationAbstractions.IRuleSlot
{
    public Task<ApplicationAbstractions.RuleOutput> ExecuteAsync(
        ApplicationAbstractions.RuleInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var analysisContext = input.Analysis.Analysis.CreateContext();
        var initialDecisions = markDecisionBuilder.BuildDecisions(analysisContext, cancellationToken);
        var predictedDecisions = referenceZeroPredictionAnalyzer.Predict(analysisContext, initialDecisions);
        return Task.FromResult(
            new ApplicationAbstractions.RuleOutput(
                input.Analysis,
                new CoreRules.DecisionSet(initialDecisions, predictedDecisions)));
    }
}

internal sealed class DecisionSlotAdapter(
    ApplicationAbstractions.IFunctionImpactAnalyzer functionImpactAnalyzer) : ApplicationAbstractions.IDecisionSlot
{
    public Task<ApplicationAbstractions.DecisionOutput> ExecuteAsync(
        ApplicationAbstractions.DecisionInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var request = input.Rule.Analysis.Load.Request;
        var planResult = CorePlanning.AuditPlanCompiler.Compile(
            new CorePlanning.PlanMetadata(
                "dome",
                "1",
                request.InputPath,
                request.OutputPath,
                (CoreCommon.RunMode)request.Mode),
            input.Rule.Decisions.AllDecisions);
        var functionImpactSet = planResult.Plan == null
            ? null
            : functionImpactAnalyzer.Analyze(planResult.Plan, input.Rule.Analysis.Analysis);
        return Task.FromResult(
            new ApplicationAbstractions.DecisionOutput(
                input.Rule,
                new CorePlanning.PlanningOutput(planResult, functionImpactSet)));
    }
}

internal sealed class ResultSlotAdapter(
    ApplicationAbstractions.IRewriteExecutor rewriteExecutor,
    IRewriteOutputStore rewriteOutputStore,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : ApplicationAbstractions.IResultSlot
{
    public async Task<ModelExecution.RunResult> ExecuteAsync(
        ApplicationAbstractions.ResultInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var request = input.Load.Request;
        return request.Mode switch
        {
            ModelPrimitives.RunMode.AnalyzeOnly => await CompleteAnalyzeOnlyAsync(input, request.OutputPath, cancellationToken),
            ModelPrimitives.RunMode.PlanOnly => await CompletePlanOnlyAsync(input, request.OutputPath, cancellationToken),
            _ => await CompleteStandardAsync(input, request.OutputPath, cancellationToken)
        };
    }

    private async Task<ModelExecution.RunResult> CompleteAnalyzeOnlyAsync(
        ApplicationAbstractions.ResultInput input,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var artifactPlan = artifactPlanBuilder.BuildAnalyzeOnlySuccess();
        var report = runReportBuilder.BuildAnalyzeOnlySuccess(
            input.Analysis.Analysis.View,
            input.Load.Workspace,
            artifactPlan.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(input.Analysis.Analysis));
        await artifactEmissionService.EmitAsync(outputPath, artifactPlan, null, report, input.Analysis.Analysis.View, cancellationToken);
        return ModelExecution.RunResult.Success(outputPath, Path.Combine(outputPath, "report.json"));
    }

    private async Task<ModelExecution.RunResult> CompletePlanOnlyAsync(
        ApplicationAbstractions.ResultInput input,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var ruleOutput = input.Rule ?? throw new InvalidOperationException("Rule output is required for plan-only result generation.");
        var decisionOutput = input.Decision ?? throw new InvalidOperationException("Decision output is required for plan-only result generation.");
        var plan = decisionOutput.Planning.Plan ?? throw new InvalidOperationException("Compiled plan is required for plan-only result generation.");
        var artifactPlan = artifactPlanBuilder.BuildPlanOnlySuccess();
        var report = runReportBuilder.BuildPlanOnlySuccess(
            input.Analysis.Analysis.View,
            input.Load.Workspace,
            ruleOutput.Decisions.AllDecisions,
            plan,
            decisionOutput.Planning.FunctionImpactSet,
            artifactPlan.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(input.Analysis.Analysis));
        await artifactEmissionService.EmitAsync(outputPath, artifactPlan, plan, report, null, cancellationToken);
        return ModelExecution.RunResult.Success(outputPath, Path.Combine(outputPath, "report.json"));
    }

    private async Task<ModelExecution.RunResult> CompleteStandardAsync(
        ApplicationAbstractions.ResultInput input,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var ruleOutput = input.Rule ?? throw new InvalidOperationException("Rule output is required for standard result generation.");
        var decisionOutput = input.Decision ?? throw new InvalidOperationException("Decision output is required for standard result generation.");
        var plan = decisionOutput.Planning.Plan ?? throw new InvalidOperationException("Compiled plan is required before rewrite.");
        var sourceSet = input.Load.Workspace.Input?.SourceSet ??
            throw new InvalidOperationException("Successful workspace load must include a source set.");
        var rewriteInputs = DomeRewritePlanProjector.BuildRewriteInputs(sourceSet, plan);

        var rewriteStart = Stopwatch.StartNew();
        progressReporter.Report($"[dome] Starting rewrite for {rewriteInputs.Count} documents...");
        var rewrittenDocuments = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new ConcurrentBag<string>();
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
                var rewriteResult = await rewriteExecutor.ExecuteAsync(rewriteInput, token);
                foreach (var diagnostic in rewriteResult.Diagnostics)
                {
                    diagnostics.Add(diagnostic);
                }

                if (!rewriteResult.IsSuccess || rewriteResult.Documents.Count == 0)
                {
                    var defaultMessage = $"Rewrite failed for '{rewriteInput.SourceSet.Documents.FirstOrDefault()?.RelativePath ?? "unknown"}'.";
                    Interlocked.CompareExchange(ref rewriteFailureMessage, rewriteResult.Message ?? defaultMessage, null);
                    return;
                }

                try
                {
                    foreach (var document in rewriteResult.Documents)
                    {
                        await rewriteOutputStore.SaveAsync(outputPath, document.RelativePath, document.SourceText, token);
                        rewrittenDocuments[document.RelativePath] = document.SourceText;
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref rewriteFailureMessage, ex.Message, null);
                    return;
                }

                var completed = Interlocked.Increment(ref completedCount);
                if (completed == rewriteInputs.Count || completed % 100 == 0)
                {
                    progressReporter.Report($"[dome] Rewrite progress {completed}/{rewriteInputs.Count} after {DomeStageFormatting.FormatElapsed(rewriteStart.Elapsed)}.");
                }
            });

        var rewrittenArtifacts = rewrittenDocuments
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new ModelExecution.RewrittenDocument(pair.Key, pair.Value))
            .ToArray();
        var emittedArtifactPaths = rewrittenArtifacts
            .Select(static document => Path.Combine("rewritten", document.RelativePath))
            .ToArray();

        if (rewriteFailureMessage != null)
        {
            var artifactPlan = artifactPlanBuilder.BuildRewriteFailure(emittedArtifactPaths);
            var report = runReportBuilder.BuildRewriteFailure(
                input.Analysis.Analysis.View,
                input.Load.Workspace,
                plan,
                rewrittenArtifacts.Length,
                new ModelExecution.PlanCoverageSummary(0, 0, Array.Empty<string>()),
                decisionOutput.Planning.FunctionImpactSet,
                ruleOutput.Decisions.InitialDecisions,
                ruleOutput.Decisions.PredictedDecisions,
                rewriteFailureMessage,
                artifactPlan.GeneratedArtifacts,
                DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(input.Analysis.Analysis));
            await artifactEmissionService.EmitAsync(outputPath, artifactPlan, plan, report, null, cancellationToken);
            return ModelExecution.RunResult.Failure(ModelPrimitives.FailureCode.RewriteFailed, outputPath, rewriteFailureMessage);
        }

        var rewrittenDocumentPaths = rewrittenArtifacts
            .Select(static document => Path.Combine("rewritten", document.RelativePath))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var artifactPlanSuccess = artifactPlanBuilder.BuildStandardSuccess(rewrittenDocumentPaths);
        var reportSuccess = runReportBuilder.BuildStandardSuccess(
            input.Analysis.Analysis.View,
            input.Load.Workspace,
            ruleOutput.Decisions.AllDecisions,
            plan,
            rewrittenDocumentPaths.Length,
            decisionOutput.Planning.FunctionImpactSet,
            artifactPlanSuccess.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(input.Analysis.Analysis));
        await artifactEmissionService.EmitAsync(outputPath, artifactPlanSuccess, plan, reportSuccess, null, cancellationToken);
        progressReporter.Report($"[dome] Run completed with {rewrittenDocumentPaths.Length} rewritten documents in {DomeStageFormatting.FormatElapsed(rewriteStart.Elapsed)}.");
        return ModelExecution.RunResult.Success(outputPath, Path.Combine(outputPath, "report.json"));
    }
}
