namespace TerrariaTools.Dome.Application.Pipeline;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Host;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CoreCpg = TerrariaTools.Dome.Core.Cpg;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreRules = TerrariaTools.Dome.Core.Rules.Model;
using TerrariaTools.Dome.Core.Rules.Services;

/// <summary>
/// Bridges a pipeline run into the public flow execution context contract.
/// </summary>
internal sealed class DomePipelineFlowExecutionContext(
    string correlationId,
    IDictionary<string, object?> items) : ApplicationAbstractions.IFlowExecutionContext
{
    public string CorrelationId { get; } = correlationId;

    public IDictionary<string, object?> Items { get; } = items;
}

internal static class DomePipelineFlowExecutionContexts
{
    private static readonly ConditionalWeakTable<DomePipelineContext, DomePipelineFlowExecutionContext> ExecutionContexts = [];

    internal static ApplicationAbstractions.IFlowExecutionContext GetOrCreate(DomePipelineContext context) =>
        ExecutionContexts.GetValue(
            context,
            static current => new DomePipelineFlowExecutionContext(
                $"{current.Request.InputPath}->{current.Request.OutputPath}",
                new Dictionary<string, object?>(StringComparer.Ordinal)));
}

/// <summary>
/// Executes the fixed Load slot and converts controlled load failures into terminal reports.
/// </summary>
internal sealed class LoadSlotStage(
    ApplicationAbstractions.ILoadSlot loadSlot,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override string StageName => "Load";

    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        progressReporter.Report($"[dome] Starting workspace load: {context.Request.InputPath}");
        var loadOutput = await loadSlot.ExecuteAsync(
            new ApplicationAbstractions.LoadInput(context.Request),
            DomePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        context.SetLoadResult(loadOutput.Workspace);
        progressReporter.Report($"[dome] Workspace load completed with {loadOutput.Workspace.Documents.Count} C# documents in {DomeStageFormatting.FormatElapsed(context.RunStopwatch.Elapsed)}.");
        if (loadOutput.Workspace.IsSuccess && loadOutput.Workspace.Documents.Count > 0)
        {
            return;
        }

        var message = loadOutput.Workspace.Diagnostics.FirstOrDefault()?.Message ?? "No C# input files were found.";
        var artifactPlan = artifactPlanBuilder.BuildWorkspaceLoadFailure();
        var report = runReportBuilder.BuildWorkspaceLoadFailure(loadOutput.Workspace, message, artifactPlan.GeneratedArtifacts);
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, null, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteFailure(context, ModelPrimitives.FailureCode.WorkspaceLoadFailed, context.Request.OutputPath, message);
    }
}

/// <summary>
/// Executes the fixed Analyze slot and converts analysis exceptions into terminal reports.
/// </summary>
internal sealed class AnalyzeSlotStage(
    ApplicationAbstractions.IAnalyzeSlot analyzeSlot,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override string StageName => "Analyze";

    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before analysis.");
        var analysisStopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Starting Roslyn analysis...");

        try
        {
            var analysisOutput = await analyzeSlot.ExecuteAsync(
                new ApplicationAbstractions.AnalyzeInput(new ApplicationAbstractions.LoadOutput(context.Request, loadResult)),
                DomePipelineFlowExecutionContexts.GetOrCreate(context),
                cancellationToken);
            context.SetAnalysisOutput(analysisOutput.Analysis);
            progressReporter.Report($"[dome] Analysis completed with {analysisOutput.Analysis.View.Targets.Count} targets in {DomeStageFormatting.FormatElapsed(analysisStopwatch.Elapsed)}.");
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

/// <summary>
/// Executes the fixed Rule slot and stores the produced decision set.
/// </summary>
internal sealed class RuleSlotStage(
    ApplicationAbstractions.IRuleSlot ruleSlot,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override string StageName => "Rule";

    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before building rules.");
        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required before building rules.");
        var stopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Building marking decisions...");

        var ruleOutput = await ruleSlot.ExecuteAsync(
            new ApplicationAbstractions.RuleInput(
                new ApplicationAbstractions.AnalyzeOutput(
                    new ApplicationAbstractions.LoadOutput(context.Request, loadResult),
                    analysisOutput)),
            DomePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        context.SetDecisions(ruleOutput.Decisions);
        progressReporter.Report($"[dome] Built {ruleOutput.Decisions.InitialDecisions.Count} initial and {ruleOutput.Decisions.PredictedDecisions.Count} predicted decisions in {DomeStageFormatting.FormatElapsed(stopwatch.Elapsed)}.");
    }
}

/// <summary>
/// Executes the fixed Decision slot and converts plan compilation failures into terminal reports.
/// </summary>
internal sealed class DecisionSlotStage(
    ApplicationAbstractions.IDecisionSlot decisionSlot,
    ArtifactPlanBuilder artifactPlanBuilder,
    RunReportBuilder runReportBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    public override string StageName => "Decision";

    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before plan compilation.");
        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required before plan compilation.");
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required before plan compilation.");

        var stopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Compiling audit plan...");
        var decisionOutput = await decisionSlot.ExecuteAsync(
            new ApplicationAbstractions.DecisionInput(
                new ApplicationAbstractions.RuleOutput(
                    new ApplicationAbstractions.AnalyzeOutput(
                        new ApplicationAbstractions.LoadOutput(context.Request, loadResult),
                        analysisOutput),
                    decisions)),
            DomePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        var planResult = decisionOutput.Planning.Compilation;
        context.SetPlanningOutput(decisionOutput.Planning);
        progressReporter.Report($"[dome] Audit plan compiled: success={planResult.IsSuccess}, changes={(planResult.Plan?.Changes.Count ?? 0)}, conflicts={planResult.Conflicts.Count}, elapsed={DomeStageFormatting.FormatElapsed(stopwatch.Elapsed)}.");

        if (planResult.IsSuccess && planResult.Plan != null)
        {
            return;
        }

        var artifactPlan = artifactPlanBuilder.BuildPlanCompileFailure();
        var report = runReportBuilder.BuildPlanCompileFailure(
            analysisOutput.View,
            loadResult,
            planResult,
            new ModelExecution.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            decisionOutput.Planning.FunctionImpactSet,
            decisions.InitialDecisions,
            decisions.PredictedDecisions,
            artifactPlan.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(analysisOutput));
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, null, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteFailure(context, ModelPrimitives.FailureCode.PlanCompileFailed, context.Request.OutputPath, planResult.Message);
    }
}

/// <summary>
/// Executes the fixed Result slot and commits its run result as the pipeline terminal state.
/// </summary>
internal sealed class ResultSlotStage(
    ApplicationAbstractions.IResultSlot resultSlot) : PipelineStage<DomePipelineContext>
{
    public override string StageName => "Result";

    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before producing the result.");
        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required before producing the result.");

        var loadOutput = new ApplicationAbstractions.LoadOutput(context.Request, loadResult);
        var analyzeOutput = new ApplicationAbstractions.AnalyzeOutput(loadOutput, analysisOutput);
        ApplicationAbstractions.RuleOutput? ruleOutput = context.Decisions is null
            ? null
            : new ApplicationAbstractions.RuleOutput(analyzeOutput, context.Decisions);
        ApplicationAbstractions.DecisionOutput? decisionOutput = context.PlanningOutput is null || ruleOutput is null
            ? null
            : new ApplicationAbstractions.DecisionOutput(ruleOutput, context.PlanningOutput);

        var result = await resultSlot.ExecuteAsync(
            new ApplicationAbstractions.ResultInput(loadOutput, analyzeOutput, ruleOutput, decisionOutput),
            DomePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        context.TerminalState = new PipelineTerminalState(result);
    }
}

/// <summary>
/// 执行工作区加载阶段，并在失败时写出终态报告。
/// </summary>
internal sealed class WorkspaceLoadStage(
    ApplicationAbstractions.IWorkspaceLoader workspaceLoader,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    /// <summary>
    /// 执行工作区加载阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
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

/// <summary>
/// 执行 Roslyn 分析阶段，并在异常时生成失败报告。
/// </summary>
internal sealed class AnalysisStage(
    ApplicationAbstractions.IAnalysisEngine analysisEngine,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    /// <summary>
    /// 执行分析阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before analysis.");
        var analysisStopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Starting Roslyn analysis...");
        try
        {
            var input = loadResult.Input ?? throw new InvalidOperationException("Successful workspace load must include analysis input.");
            var analysisOutput = await analysisEngine.AnalyzeAsync(input, cancellationToken);
            context.SetAnalysisOutput(analysisOutput);
            progressReporter.Report($"[dome] Analysis completed with {analysisOutput.View.Targets.Count} targets in {DomeStageFormatting.FormatElapsed(analysisStopwatch.Elapsed)}.");
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

/// <summary>
/// 在仅分析模式下写出分析产物并结束流程。
/// </summary>
internal sealed class AnalyzeOnlyFinalizeStage(
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService) : PipelineStage<DomePipelineContext>
{
    /// <summary>
    /// 执行仅分析模式的收尾阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Mode != ModelPrimitives.RunMode.AnalyzeOnly)
        {
            return;
        }

        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required for analyze-only finalize.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required for analyze-only finalize.");
        var artifactPlan = artifactPlanBuilder.BuildAnalyzeOnlySuccess();
        var report = runReportBuilder.BuildAnalyzeOnlySuccess(
            analysisOutput.View,
            loadResult,
            artifactPlan.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(analysisOutput));
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, null, report, analysisOutput.View, cancellationToken);
        DomeTerminalCompletion.CompleteSuccess(context, context.Request.OutputPath);
    }
}

/// <summary>
/// 构建标记决策并追加预测决策。
/// </summary>
internal sealed class MarkDecisionsStage(
    IMarkDecisionBuilder markDecisionBuilder,
    ApplicationAbstractions.IReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    /// <summary>
    /// 执行标记决策构建阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required before marking decisions.");
        var stopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Building marking decisions...");
        var analysisContext = analysisOutput.CreateContext();
        var initialDecisions = markDecisionBuilder.BuildDecisions(analysisContext, cancellationToken);
        var predictedDecisions = referenceZeroPredictionAnalyzer.Predict(analysisContext, initialDecisions);
        context.SetDecisions(new CoreRules.DecisionSet(initialDecisions, predictedDecisions));
        progressReporter.Report($"[dome] Built {initialDecisions.Count} initial and {predictedDecisions.Count} predicted decisions in {DomeStageFormatting.FormatElapsed(stopwatch.Elapsed)}.");
        return Task.CompletedTask;
    }
}

/// <summary>
/// 编译审计计划并在失败时输出冲突报告。
/// </summary>
internal sealed class CompilePlanStage(
    ApplicationAbstractions.IFunctionImpactAnalyzer functionImpactAnalyzer,
    ArtifactPlanBuilder artifactPlanBuilder,
    RunReportBuilder runReportBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    /// <summary>
    /// 执行计划编译阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required before plan compilation.");
        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required before plan compilation.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before plan compilation.");

        var stopwatch = Stopwatch.StartNew();
        progressReporter.Report("[dome] Compiling audit plan...");
        var planResult = CorePlanning.AuditPlanCompiler.Compile(
            new CorePlanning.PlanMetadata("dome", "1", context.Request.InputPath, context.Request.OutputPath, (CoreCommon.RunMode)context.Request.Mode),
            decisions.AllDecisions);
        var functionImpactSet = planResult.Plan == null
            ? null
            : functionImpactAnalyzer.Analyze(planResult.Plan, analysisOutput);
        context.SetPlanningOutput(new CorePlanning.PlanningOutput(planResult, functionImpactSet));
        progressReporter.Report($"[dome] Audit plan compiled: success={planResult.IsSuccess}, changes={(planResult.Plan?.Changes.Count ?? 0)}, conflicts={planResult.Conflicts.Count}, elapsed={DomeStageFormatting.FormatElapsed(stopwatch.Elapsed)}.");

        if (planResult.IsSuccess && planResult.Plan != null)
        {
            return;
        }

        var artifactPlan = artifactPlanBuilder.BuildPlanCompileFailure();
        var report = runReportBuilder.BuildPlanCompileFailure(
            analysisOutput.View,
            loadResult,
            planResult,
            new ModelExecution.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            functionImpactSet,
            decisions.InitialDecisions,
            decisions.PredictedDecisions,
            artifactPlan.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(analysisOutput));
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, null, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteFailure(context, ModelPrimitives.FailureCode.PlanCompileFailed, context.Request.OutputPath, planResult.Message);
    }
}

/// <summary>
/// 在仅计划模式下写出计划产物并结束流程。
/// </summary>
internal sealed class PlanOnlyFinalizeStage(
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService) : PipelineStage<DomePipelineContext>
{
    /// <summary>
    /// 执行仅计划模式的收尾阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Mode != ModelPrimitives.RunMode.PlanOnly)
        {
            return;
        }

        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required for plan-only finalize.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required for plan-only finalize.");
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required for plan-only finalize.");
        var planningOutput = context.PlanningOutput ?? throw new InvalidOperationException("Planning output is required for plan-only finalize.");
        var plan = planningOutput.Plan ?? throw new InvalidOperationException("Compiled plan is required for plan-only finalize.");
        var artifactPlan = artifactPlanBuilder.BuildPlanOnlySuccess();
        var report = runReportBuilder.BuildPlanOnlySuccess(
            analysisOutput.View,
            loadResult,
            decisions.AllDecisions,
            plan,
            planningOutput.FunctionImpactSet,
            artifactPlan.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(analysisOutput));
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, plan, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteSuccess(context, context.Request.OutputPath);
    }
}

/// <summary>
/// 执行源码重写并在失败时输出中间产物。
/// </summary>
internal sealed class RewriteStage(
    ApplicationAbstractions.IRewriteExecutor rewriteExecutor,
    IRewriteOutputStore rewriteOutputStore,
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    /// <summary>
    /// 执行重写阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Mode != ModelPrimitives.RunMode.Standard)
        {
            return;
        }

        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required before rewrite.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required before rewrite.");
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required before rewrite.");
        var planningOutput = context.PlanningOutput ?? throw new InvalidOperationException("Planning output is required before rewrite.");
        var plan = planningOutput.Plan ?? throw new InvalidOperationException("Compiled plan is required before rewrite.");
        var sourceSet = loadResult.Input?.SourceSet ?? throw new InvalidOperationException("Successful workspace load must include a source set.");

        var rewriteInputs = DomeRewritePlanProjector.BuildRewriteInputs(sourceSet, plan);
        var rewriteStopwatch = Stopwatch.StartNew();
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
                        await rewriteOutputStore.SaveAsync(context.Request.OutputPath, document.RelativePath, document.SourceText, token);
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
                    progressReporter.Report($"[dome] Rewrite progress {completed}/{rewriteInputs.Count} after {DomeStageFormatting.FormatElapsed(rewriteStopwatch.Elapsed)}.");
                }
            });

        var distinctDocuments = rewrittenDocuments
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new ModelExecution.RewrittenDocument(pair.Key, pair.Value))
            .ToArray();
        var emittedArtifactPaths = distinctDocuments
            .Select(static document => Path.Combine("rewritten", document.RelativePath))
            .ToArray();
        var rewriteOutput = rewriteFailureMessage == null
            ? ModelExecution.RewriteOutput.Success(distinctDocuments, diagnostics.ToArray())
            : ModelExecution.RewriteOutput.Failure(ModelPrimitives.FailureCode.RewriteFailed, rewriteFailureMessage, diagnostics.ToArray());
        context.SetRewriteOutput(rewriteOutput);

        if (rewriteFailureMessage == null)
        {
            return;
        }

        var artifactPlan = artifactPlanBuilder.BuildRewriteFailure(emittedArtifactPaths);
        var report = runReportBuilder.BuildRewriteFailure(
            analysisOutput.View,
            loadResult,
            plan,
            distinctDocuments.Length,
            new ModelExecution.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            planningOutput.FunctionImpactSet,
            decisions.InitialDecisions,
            decisions.PredictedDecisions,
            rewriteFailureMessage,
            artifactPlan.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(analysisOutput));
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, plan, report, null, cancellationToken);
        DomeTerminalCompletion.CompleteFailure(context, ModelPrimitives.FailureCode.RewriteFailed, context.Request.OutputPath, rewriteFailureMessage);
    }
}

/// <summary>
/// 在标准模式下写出最终产物并结束流程。
/// </summary>
internal sealed class StandardFinalizeStage(
    RunReportBuilder runReportBuilder,
    ArtifactPlanBuilder artifactPlanBuilder,
    IArtifactEmissionService artifactEmissionService,
    IDomeProgressReporter progressReporter) : PipelineStage<DomePipelineContext>
{
    /// <summary>
    /// 执行标准模式的收尾阶段。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(DomePipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Mode != ModelPrimitives.RunMode.Standard)
        {
            return;
        }

        var analysisOutput = context.AnalysisOutput ?? throw new InvalidOperationException("Analysis output is required for standard finalize.");
        var loadResult = context.LoadResult ?? throw new InvalidOperationException("Workspace load result is required for standard finalize.");
        var decisions = context.Decisions ?? throw new InvalidOperationException("Decisions are required for standard finalize.");
        var planningOutput = context.PlanningOutput ?? throw new InvalidOperationException("Planning output is required for standard finalize.");
        var plan = planningOutput.Plan ?? throw new InvalidOperationException("Compiled plan is required for standard finalize.");
        var rewriteOutput = context.RewriteOutput ?? throw new InvalidOperationException("Rewrite output is required for standard finalize.");

        var rewrittenDocumentPaths = rewriteOutput.Documents
            .Select(document => Path.Combine("rewritten", document.RelativePath))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var artifactPlan = artifactPlanBuilder.BuildStandardSuccess(rewrittenDocumentPaths);
        var report = runReportBuilder.BuildStandardSuccess(
            analysisOutput.View,
            loadResult,
            decisions.AllDecisions,
            plan,
            rewrittenDocumentPaths.Length,
            planningOutput.FunctionImpactSet,
            artifactPlan.GeneratedArtifacts,
            DomeApplicationStageHelpers.BuildAdvancedAnalysisSummary(analysisOutput));
        await artifactEmissionService.EmitAsync(context.Request.OutputPath, artifactPlan, plan, report, null, cancellationToken);
        progressReporter.Report($"[dome] Run completed with {rewrittenDocumentPaths.Length} rewritten documents in {DomeStageFormatting.FormatElapsed(context.RunStopwatch.Elapsed)}.");
        DomeTerminalCompletion.CompleteSuccess(context, context.Request.OutputPath);
    }
}

/// <summary>
/// 将审计计划按文档投影为重写输入集合。
/// </summary>
internal static class DomeRewritePlanProjector
{
    /// <summary>
    /// 为每个源码文档构建对应的重写输入。
    /// </summary>
    /// <param name="sourceSet">原始源码集合。</param>
    /// <param name="plan">完整审计计划。</param>
    /// <returns>按文档拆分后的重写输入集合。</returns>
    internal static IReadOnlyList<ModelExecution.RewriteInput> BuildRewriteInputs(CoreAnalysis.SourceDocumentSet sourceSet, CorePlanning.AuditPlan plan)
    {
        return sourceSet.Documents
            .Select(document => new ModelExecution.RewriteInput(
                new CoreAnalysis.SourceDocumentSet(
                    sourceSet.EntryPath,
                    sourceSet.RootPath,
                    [document]),
                new CorePlanning.AuditPlan(
                    plan.Metadata,
                    plan.Changes.Where(change => change.Target.DocumentPath == document.RelativePath).ToArray(),
                    plan.Conflicts)))
            .ToArray();
    }
}

/// <summary>
/// 提供 Dome 阶段日志使用的格式化工具。
/// </summary>
internal static class DomeStageFormatting
{
    /// <summary>
    /// 将耗时格式化为适合日志输出的文本。
    /// </summary>
    /// <param name="elapsed">耗时。</param>
    /// <returns>格式化后的耗时文本。</returns>
    public static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1} s"
            : $"{elapsed.TotalMilliseconds:F0} ms";
}

/// <summary>
/// 封装标准 Dome 流水线的终态写入逻辑。
/// </summary>
internal static class DomeTerminalCompletion
{
    /// <summary>
    /// 将上下文标记为成功终态。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="outputPath">输出目录。</param>
    internal static void CompleteSuccess(DomePipelineContext context, string outputPath)
    {
        context.TerminalState = new PipelineTerminalState(
            ModelExecution.RunResult.Success(outputPath, Path.Combine(outputPath, "report.json")));
    }

    /// <summary>
    /// 将上下文标记为失败终态。
    /// </summary>
    /// <param name="context">流水线上下文。</param>
    /// <param name="failureCode">失败代码。</param>
    /// <param name="outputPath">输出目录。</param>
    /// <param name="message">失败消息。</param>
    internal static void CompleteFailure(DomePipelineContext context, ModelPrimitives.FailureCode failureCode, string outputPath, string? message)
    {
        context.TerminalState = new PipelineTerminalState(
            ModelExecution.RunResult.Failure(failureCode, outputPath, message));
    }
}

/// <summary>
/// 提供标准 Dome 阶段共享的辅助逻辑。
/// </summary>
internal static class DomeApplicationStageHelpers
{
    /// <summary>
    /// 从分析输出构建高级分析摘要。
    /// </summary>
    /// <param name="analysisOutput">分析输出。</param>
    /// <returns>高级分析摘要。</returns>
    internal static CoreAnalysis.AdvancedAnalysisSummary BuildAdvancedAnalysisSummary(CoreAnalysis.AnalysisOutput analysisOutput) =>
        BuildAdvancedAnalysisSummary(
            analysisOutput.Services.AdvancedAnalysis.BuildSummary(),
            analysisOutput.CodePropertyGraph);

    internal static CoreAnalysis.AdvancedAnalysisSummary BuildAdvancedAnalysisSummary(
        CoreAnalysis.AdvancedAnalysisSummary summary,
        CoreCpg.DomeCpg codePropertyGraph)
    {
        var notes = (summary.Notes ?? Array.Empty<string>())
            .Concat(BuildCpgFingerprintNotes(codePropertyGraph))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return summary with { Notes = notes };
    }

    private static IReadOnlyList<string> BuildCpgFingerprintNotes(CoreCpg.DomeCpg codePropertyGraph)
    {
        var callEdgeCount = codePropertyGraph.Edges.Count(edge => edge.Label == CoreCpg.EdgeKinds.Call);
        var metaData = codePropertyGraph.GetNodesByKind<CoreCpg.MetaDataNode>(CoreCpg.NodeKinds.MetaData)
            .FirstOrDefault();
        var overlays = metaData is null || metaData.Overlays.Count == 0
            ? "none"
            : string.Join("|", metaData.Overlays.OrderBy(static overlay => overlay, StringComparer.Ordinal));

        return
        [
            $"CpgCallEdges={callEdgeCount}",
            $"CpgOverlays={overlays}"
        ];
    }
}

