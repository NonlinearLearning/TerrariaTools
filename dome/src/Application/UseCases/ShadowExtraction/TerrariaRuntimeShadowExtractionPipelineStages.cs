namespace TerrariaTools.Dome.Application.UseCases.ShadowExtraction;

using System.Runtime.CompilerServices;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Composition;
using TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// 解析影子提取输入。
/// </summary>
internal sealed class ResolveInputStage(
    IShadowExtractionInputResolver inputResolver,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    /// <summary>
    /// 执行输入解析阶段。
    /// </summary>
    /// <param name="context">影子提取流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = await inputResolver.ResolveAsync(context.Request, progressReporter, cancellationToken);
        if (!inputResolution.IsSuccess || inputResolution.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Application.Ports.FailureCode)inputResolution.FailureCode, context.OutputRootPath, inputResolution.Message);
            return;
        }

        context.SetInputResolution(inputResolution.Value);
    }
}

/// <summary>
/// 执行影子提取分析。
/// </summary>
internal sealed class AnalyzeShadowStage(
    IShadowExtractionAnalysisStage analysisStage,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    /// <summary>
    /// 执行影子分析阶段。
    /// </summary>
    /// <param name="context">影子提取流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = context.InputResolution ?? throw new InvalidOperationException("Input resolution is required before shadow analysis.");
        var analysis = await analysisStage.AnalyzeAsync(inputResolution, progressReporter, cancellationToken);
        if (!analysis.IsSuccess || analysis.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Application.Ports.FailureCode)analysis.FailureCode, inputResolution.Layout.OutputRootPath, analysis.Message);
            return;
        }

        context.SetAnalysis(analysis.Value);
    }
}

/// <summary>
/// 构建影子闭包计划。
/// </summary>
internal sealed class BuildClosureStage(
    IShadowClosurePlanner closurePlanner,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    /// <summary>
    /// 执行闭包计划构建阶段。
    /// </summary>
    /// <param name="context">影子提取流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var analysis = context.Analysis ?? throw new InvalidOperationException("Analysis is required before closure planning.");
        var closurePlan = closurePlanner.BuildPlan(analysis, progressReporter, cancellationToken);
        if (!closurePlan.IsSuccess || closurePlan.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Application.Ports.FailureCode)closurePlan.FailureCode, analysis.Input.Layout.OutputRootPath, closurePlan.Message);
            return Task.CompletedTask;
        }

        context.SetClosurePlan(closurePlan.Value);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 写入影子工作区内容。
/// </summary>
internal sealed class WriteShadowWorkspaceStage(
    IShadowWorkspaceWriter workspaceWriter,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    /// <summary>
    /// 执行影子工作区写入阶段。
    /// </summary>
    /// <param name="context">影子提取流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = context.InputResolution ?? throw new InvalidOperationException("Input resolution is required before workspace write.");
        var analysis = context.Analysis ?? throw new InvalidOperationException("Analysis is required before workspace write.");
        var closurePlan = context.ClosurePlan ?? throw new InvalidOperationException("Closure plan is required before workspace write.");
        var workspaceWrite = await workspaceWriter.WriteAsync(inputResolution, analysis, closurePlan, progressReporter, cancellationToken);
        if (!workspaceWrite.IsSuccess || workspaceWrite.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Application.Ports.FailureCode)workspaceWrite.FailureCode, inputResolution.Layout.OutputRootPath, workspaceWrite.Message);
            return;
        }

        context.SetWorkspaceWriteResult(workspaceWrite.Value);
        progressReporter.Report($"[tr-shadow] Rewrite summary: preserved={workspaceWrite.Value.RewriteSummary.PreservedMembers}, defaulted={workspaceWrite.Value.RewriteSummary.DefaultedMembers}, emptied={workspaceWrite.Value.RewriteSummary.EmptiedMembers}");
    }
}

/// <summary>
/// 根据当前流水线状态生成影子提取报告。
/// </summary>
internal sealed class BuildShadowReportStage(IShadowExtractionReportBuilder reportBuilder) : PipelineStage<ShadowExtractionPipelineContext>
{
    /// <summary>
    /// 执行影子提取报告构建阶段。
    /// </summary>
    /// <param name="context">影子提取流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        context.SetReport(reportBuilder.Build(
            context.InputResolution ?? throw new InvalidOperationException("Input resolution is required before report build."),
            context.Analysis ?? throw new InvalidOperationException("Analysis is required before report build."),
            context.ClosurePlan ?? throw new InvalidOperationException("Closure plan is required before report build."),
            context.WorkspaceWriteResult ?? throw new InvalidOperationException("Workspace write result is required before report build.")));
        return Task.CompletedTask;
    }
}

/// <summary>
/// 构建影子工作区解决方案。
/// </summary>
internal sealed class BuildShadowWorkspaceStage(
    ITerrariaRuntimeBuildExecutor buildExecutor,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    /// <summary>
    /// 执行影子工作区构建阶段。
    /// </summary>
    /// <param name="context">影子提取流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.InputResolution?.Layout ?? throw new InvalidOperationException("Input resolution is required before build.");
        progressReporter.Report("[tr-shadow] Building shadow workspace...");
        context.SetBuildSummary(await buildExecutor.ExecuteAsync(TerrariaRuntimeShadowStageHelpers.ToRuntimeLayout(layout), progressReporter, cancellationToken));
        context.UpdateReport((context.Report ?? throw new InvalidOperationException("Report is required before build update.")) with { TrBuildSummary = context.BuildSummary });
    }
}

/// <summary>
/// 将影子提取报告持久化到产物目录。
/// </summary>
internal sealed class PersistShadowReportStage(
    IShadowExtractionReportStore reportStore,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    /// <summary>
    /// 执行影子提取报告持久化阶段。
    /// </summary>
    /// <param name="context">影子提取流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.InputResolution?.Layout ?? throw new InvalidOperationException("Input resolution is required before report persistence.");
        context.SetReportPath(Path.Combine(layout.ArtifactsPath, "shadow-report.json"));
        progressReporter.Report("[tr-shadow] Persisting shadow report...");
        await reportStore.SaveAsync(
            context.ReportPath,
            context.Report ?? throw new InvalidOperationException("Report is required before persistence."),
            cancellationToken);
    }
}

/// <summary>
/// 根据构建结果为影子提取流水线写入终态。
/// </summary>
internal sealed class FinalizeShadowRunStage(ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    /// <summary>
    /// 执行影子提取流水线收尾阶段。
    /// </summary>
    /// <param name="context">影子提取流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = context.InputResolution ?? throw new InvalidOperationException("Input resolution is required before finalization.");
        var buildSummary = context.BuildSummary ?? throw new InvalidOperationException("Build summary is required before finalization.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before finalization.");
        if (!buildSummary.BuildSucceeded)
        {
            progressReporter.Report($"[tr-shadow] Build failed with {TerrariaRuntimeShadowStageHelpers.CountBuildErrors(buildSummary.StandardOutput, buildSummary.StandardError)} reported errors.");
            SetFailure(context, TerrariaTools.Dome.Application.Ports.FailureCode.BuildFailed, inputResolution.Layout.OutputRootPath, buildSummary.StandardError);
            return Task.CompletedTask;
        }

        progressReporter.Report("[tr-shadow] Shadow extraction pipeline completed.");
        context.TerminalState = new PipelineTerminalState(ModelExecution.RunResult.Success(inputResolution.Layout.OutputRootPath, reportPath));
        return Task.CompletedTask;
    }
}

/// <summary>
/// 提供影子提取流水线共享的辅助逻辑。
/// </summary>
internal static class TerrariaRuntimeShadowStageHelpers
{
    /// <summary>
    /// 将影子布局转换为标准运行时布局。
    /// </summary>
    /// <param name="layout">影子工作区布局。</param>
    /// <returns>等价的标准运行时布局。</returns>
    public static ApplicationAbstractions.TerrariaRuntimeLayout ToRuntimeLayout(ApplicationAbstractions.TerrariaRuntimeShadowLayout layout)
    {
        return new ApplicationAbstractions.TerrariaRuntimeLayout(
            layout.SolutionPath,
            layout.SourceRootPath,
            layout.OutputRootPath,
            layout.DependencyEnvironmentPath,
            layout.WorkspacePath,
            layout.ArtifactsPath,
            layout.WorkspaceSolutionPath);
    }

    /// <summary>
    /// 统计构建输出中的错误数量。
    /// </summary>
    /// <param name="standardOutput">标准输出内容。</param>
    /// <param name="standardError">标准错误内容。</param>
    /// <returns>识别到的错误数量。</returns>
    public static int CountBuildErrors(string standardOutput, string standardError)
    {
        return CountOccurrences(standardOutput, ": error ") + CountOccurrences(standardError, ": error ");
    }

    /// <summary>
    /// 统计文本中某个标记出现的次数。
    /// </summary>
    /// <param name="text">待搜索文本。</param>
    /// <param name="marker">要统计的标记。</param>
    /// <returns>标记出现次数。</returns>
    private static int CountOccurrences(string text, string marker)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }
}

internal sealed class ShadowExtractionPipelineFlowExecutionContext : ApplicationAbstractions.IFlowExecutionContext
{
    private readonly Dictionary<string, object?> items = new(StringComparer.Ordinal);

    internal ShadowExtractionPipelineFlowExecutionContext(string correlationId)
    {
        CorrelationId = correlationId;
    }

    public string CorrelationId { get; }

    public IDictionary<string, object?> Items => items;
}

internal static class ShadowExtractionPipelineFlowExecutionContexts
{
    private static readonly ConditionalWeakTable<ShadowExtractionPipelineContext, ShadowExtractionPipelineFlowExecutionContext> Contexts = new();

    public static ApplicationAbstractions.IFlowExecutionContext GetOrCreate(ShadowExtractionPipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Contexts.GetValue(
            context,
            static _ => new ShadowExtractionPipelineFlowExecutionContext(Guid.NewGuid().ToString("n")));
    }
}

internal sealed class ResolveInputShadowSlotStage(ITerrariaRuntimeShadowResolveInputSlot slot) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var output = await slot.ExecuteAsync(
            new ShadowResolveInputInput(context.Request),
            ShadowExtractionPipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        if (output.FailureCode.HasValue || output.InputResolution == null)
        {
            SetFailure(
                context,
                output.FailureCode ?? ApplicationAbstractions.FailureCode.WorkspaceLoadFailed,
                context.OutputRootPath,
                output.Message);
            return;
        }

        context.SetInputResolution(output.InputResolution);
    }
}

internal sealed class AnalyzeShadowSlotStage(ITerrariaRuntimeShadowAnalyzeSlot slot) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = context.InputResolution
            ?? throw new InvalidOperationException("Input resolution is required before shadow analysis.");
        var output = await slot.ExecuteAsync(
            new ShadowAnalyzeInput(new ShadowResolveInputOutput(inputResolution)),
            ShadowExtractionPipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        if (output.FailureCode.HasValue || output.Analysis == null)
        {
            SetFailure(
                context,
                output.FailureCode ?? ApplicationAbstractions.FailureCode.AnalysisFailed,
                inputResolution.Layout.OutputRootPath,
                output.Message);
            return;
        }

        context.SetAnalysis(output.Analysis);
    }
}

internal sealed class BuildClosureShadowSlotStage(ITerrariaRuntimeShadowBuildClosureSlot slot) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var analysis = context.Analysis
            ?? throw new InvalidOperationException("Analysis is required before closure planning.");
        var output = await slot.ExecuteAsync(
            new ShadowBuildClosureInput(new ShadowAnalyzeOutput(analysis)),
            ShadowExtractionPipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        if (output.FailureCode.HasValue || output.ClosurePlan == null)
        {
            SetFailure(
                context,
                output.FailureCode ?? ApplicationAbstractions.FailureCode.AnalysisFailed,
                analysis.Input.Layout.OutputRootPath,
                output.Message);
            return;
        }

        context.SetClosurePlan(output.ClosurePlan);
    }
}

internal sealed class WriteWorkspaceShadowSlotStage(ITerrariaRuntimeShadowWriteWorkspaceSlot slot) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var analysis = context.Analysis
            ?? throw new InvalidOperationException("Analysis is required before workspace write.");
        var closurePlan = context.ClosurePlan
            ?? throw new InvalidOperationException("Closure plan is required before workspace write.");
        var output = await slot.ExecuteAsync(
            new ShadowWriteWorkspaceInput(new ShadowBuildClosureOutput(analysis, closurePlan)),
            ShadowExtractionPipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        if (output.FailureCode.HasValue || output.WorkspaceWriteResult == null)
        {
            SetFailure(
                context,
                output.FailureCode ?? ApplicationAbstractions.FailureCode.RewriteFailed,
                analysis.Input.Layout.OutputRootPath,
                output.Message);
            return;
        }

        context.SetWorkspaceWriteResult(output.WorkspaceWriteResult);
    }
}

internal sealed class BuildShadowSlotStage(ITerrariaRuntimeShadowBuildSlot slot) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var analysis = context.Analysis
            ?? throw new InvalidOperationException("Analysis is required before build.");
        var closurePlan = context.ClosurePlan
            ?? throw new InvalidOperationException("Closure plan is required before build.");
        var workspaceWriteResult = context.WorkspaceWriteResult
            ?? throw new InvalidOperationException("Workspace write result is required before build.");
        var output = await slot.ExecuteAsync(
            new ShadowBuildInput(new ShadowWriteWorkspaceOutput(
                new ShadowBuildClosureOutput(analysis, closurePlan),
                workspaceWriteResult)),
            ShadowExtractionPipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        context.SetReport(output.Report);
        context.SetBuildSummary(output.BuildSummary);
    }
}

internal sealed class PersistShadowSlotStage(ITerrariaRuntimeShadowPersistSlot slot) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var analysis = context.Analysis
            ?? throw new InvalidOperationException("Analysis is required before persistence.");
        var closurePlan = context.ClosurePlan
            ?? throw new InvalidOperationException("Closure plan is required before persistence.");
        var workspaceWriteResult = context.WorkspaceWriteResult
            ?? throw new InvalidOperationException("Workspace write result is required before persistence.");
        var report = context.Report
            ?? throw new InvalidOperationException("Report is required before persistence.");
        var buildSummary = context.BuildSummary
            ?? throw new InvalidOperationException("Build summary is required before persistence.");
        var result = await slot.ExecuteAsync(
            new ShadowPersistInput(new ShadowBuildOutput(
                new ShadowWriteWorkspaceOutput(
                    new ShadowBuildClosureOutput(analysis, closurePlan),
                    workspaceWriteResult),
                report,
                buildSummary)),
            ShadowExtractionPipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(result.ReportPath) && context.ReportPath == null)
        {
            context.SetReportPath(result.ReportPath);
        }

        context.TerminalState = new PipelineTerminalState(result);
    }
}
