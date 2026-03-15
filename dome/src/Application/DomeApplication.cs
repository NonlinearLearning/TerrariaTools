namespace TerrariaTools.Dome.Application;

using System.Collections.Concurrent;
using System.Diagnostics;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

/// <summary>
/// Dome 应用流程编排入口。
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
    private readonly IDomeProgressReporter _progressReporter;

    /// <summary>
    /// 初始化 Dome 应用实例。
    /// </summary>
    public DomeApplication(
        IWorkspaceLoader workspaceLoader,
        RoslynAnalysisEngine analysisEngine,
        FunctionImpactAnalyzer functionImpactAnalyzer,
        ReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer,
        MarkingRuleEngine markingRuleEngine,
        RoslynRewriteExecutor rewriteExecutor,
        RunReportBuilder runReportBuilder,
        ArtifactPlanBuilder artifactPlanBuilder,
        JsonArtifactWriter artifactWriter,
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
        _artifactWriter = artifactWriter;
        _progressReporter = progressReporter ?? NullDomeProgressReporter.Instance;
    }

    /// <summary>
    /// 执行完整运行流程。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果。</returns>
    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var runStopwatch = Stopwatch.StartNew();
        _progressReporter.Report($"[dome] 开始加载工作区：{request.InputPath}");
        var loadResult = await _workspaceLoader.LoadAsync(request.InputPath, request.WorkspaceLoadOptions, cancellationToken);
        _progressReporter.Report($"[dome] 工作区加载完成：共 {loadResult.Documents.Count} 个 C# 文档，模式 {loadResult.LoadMode}，耗时 {FormatElapsed(runStopwatch.Elapsed)}。");
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
            var analysisStopwatch = Stopwatch.StartNew();
            _progressReporter.Report("[dome] 开始执行 Roslyn 分析...");
            analysisResult = await _analysisEngine.AnalyzeAsync(
                loadResult.AnalysisInput ?? new SourceOnlyAnalysisInput(request.InputPath, loadResult.Documents),
                cancellationToken);
            _progressReporter.Report($"[dome] Roslyn 分析完成：生成 {analysisResult.View.Targets.Count} 个目标，耗时 {FormatElapsed(analysisStopwatch.Elapsed)}。");
            _progressReporter.Report(
                $"[dome] Roslyn 分析拆分：文档 {analysisResult.PerformanceSummary.DocumentCount} 个，" +
                $"语法索引 {FormatElapsed(analysisResult.PerformanceSummary.SyntaxIndexTime)}，" +
                $"类型图 {FormatElapsed(analysisResult.PerformanceSummary.TypeGraphTime)}，" +
                $"函数节点 {FormatElapsed(analysisResult.PerformanceSummary.FunctionNodeTime)}，" +
                $"类型体依赖 {FormatElapsed(analysisResult.PerformanceSummary.TypeBodyGraphTime)}，" +
                $"目标分析 {FormatElapsed(analysisResult.PerformanceSummary.TargetAnalysisTime)}，" +
                $"函数事实 {FormatElapsed(analysisResult.PerformanceSummary.FunctionFactsTime)}，" +
                $"结果汇总 {FormatElapsed(analysisResult.PerformanceSummary.MergeTime)}。");
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

        var markingStopwatch = Stopwatch.StartNew();
        _progressReporter.Report("[dome] 开始执行规则标记与引用归零预测...");
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
        _progressReporter.Report($"[dome] 规则标记完成：直接决策 {initialDecisions.Count} 条，预测决策 {predictedDecisions.Count} 条，合计 {decisions.Length} 条，耗时 {FormatElapsed(markingStopwatch.Elapsed)}。");

        var planStopwatch = Stopwatch.StartNew();
        _progressReporter.Report("[dome] 开始编译审计计划...");
        var planResult = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", request.InputPath, request.OutputPath, request.Mode),
            decisions);
        _progressReporter.Report($"[dome] 审计计划编译完成：成功={planResult.IsSuccess}，变更数={(planResult.Plan?.Changes.Count ?? 0)}，冲突数={planResult.Conflicts.Count}，耗时 {FormatElapsed(planStopwatch.Elapsed)}。");

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

        var rewriteStopwatch = Stopwatch.StartNew();
        _progressReporter.Report($"[dome] 开始重写源码：共 {analysisResult.Documents.Count} 个文档。");
        var rewrittenDocuments = new ConcurrentBag<string>();
        var rewriteInputs = analysisResult.Documents
            .Select(document => new DocumentRewriteInput(
                document,
                new AuditPlan(
                    planResult.Plan.Metadata,
                    planResult.Plan.Changes.Where(change => change.Target.DocumentPath == document.Document.RelativePath).ToArray(),
                    planResult.Plan.Conflicts)))
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

                var outputPath = Path.Combine(request.OutputPath, "rewritten", rewriteInput.Document.Document.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, rewriteResult.RewrittenSource, token);
                rewrittenDocuments.Add(Path.Combine("rewritten", rewriteInput.Document.Document.RelativePath));

                var completed = Interlocked.Increment(ref completedCount);
                if (completed == rewriteInputs.Length || completed % 100 == 0)
                {
                    _progressReporter.Report($"[dome] 源码重写进度：{completed}/{rewriteInputs.Length}，当前耗时 {FormatElapsed(rewriteStopwatch.Elapsed)}。");
                }
            });

        if (rewriteFailureMessage != null)
        {
            var rewrittenDocumentList = rewrittenDocuments
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var artifactPlan = _artifactPlanBuilder.BuildRewriteFailure(rewrittenDocumentList);
            var report = _runReportBuilder.BuildRewriteFailure(
                analysisResult.View,
                loadResult,
                planResult.Plan,
                rewrittenDocumentList.Length,
                new PlanCoverageSummary(0, 0, Array.Empty<string>()),
                functionImpactSet,
                initialDecisions,
                predictedDecisions,
                rewriteFailureMessage,
                artifactPlan.GeneratedArtifacts);
            await WriteArtifactsAsync(request.OutputPath, artifactPlan, planResult.Plan, report, null, cancellationToken);
            return RunResult.Failure(FailureCode.RewriteFailed, request.OutputPath, rewriteFailureMessage);
        }

        var rewrittenDocumentListForSuccess = rewrittenDocuments
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var successArtifactPlan = _artifactPlanBuilder.BuildStandardSuccess(rewrittenDocumentListForSuccess);
        var finalReport = _runReportBuilder.BuildStandardSuccess(
            analysisResult.View,
            loadResult,
            decisions,
            planResult.Plan,
            rewrittenDocumentListForSuccess.Length,
            functionImpactSet,
            successArtifactPlan.GeneratedArtifacts);
        await WriteArtifactsAsync(request.OutputPath, successArtifactPlan, planResult.Plan, finalReport, null, cancellationToken);
        _progressReporter.Report($"[dome] 运行完成：共改写 {rewrittenDocumentListForSuccess.Length} 个文档，总耗时 {FormatElapsed(runStopwatch.Elapsed)}。");
        return RunResult.Success(request.OutputPath, Path.Combine(request.OutputPath, "report.json"));
    }

    /// <summary>
    /// 文档改写输入。
    /// </summary>
    private sealed record DocumentRewriteInput(
        RoslynAnalysisDocument Document,
        AuditPlan Plan);

    /// <summary>
    /// 格式化耗时文本。
    /// </summary>
    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1} 秒"
            : $"{elapsed.TotalMilliseconds:F0} 毫秒";

    /// <summary>
    /// 写入分析、计划与报告产物。
    /// </summary>
    private async Task WriteArtifactsAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        AuditPlan? plan,
        RunReport report,
        AnalysisResultModel? analysisView,
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
