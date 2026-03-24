namespace TerrariaTools.Dome.Application.UseCases.Runtime;

using System.Runtime.CompilerServices;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Composition;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;

/// <summary>
/// 创建运行时工作区布局。
/// </summary>
internal sealed class CreateLayoutStage(ITerrariaRuntimeLayoutFactory layoutFactory) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行布局创建阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        context.SetLayout(layoutFactory.Create(context.Request));
        return Task.CompletedTask;
    }
}

/// <summary>
/// 确保运行时输出目录已创建。
/// </summary>
internal sealed class EnsureOutputDirectoriesStage(ITerrariaRuntimeWorkspacePreparer workspacePreparer) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行输出目录准备阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken) =>
        workspacePreparer.EnsureOutputDirectoriesAsync(
            context.Layout ?? throw new InvalidOperationException("Runtime layout is required before ensuring directories."),
            cancellationToken);
}

/// <summary>
/// 刷新运行时依赖环境。
/// </summary>
internal sealed class RefreshDependencyEnvironmentStage(
    ITerrariaRuntimeWorkspacePreparer workspacePreparer,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行依赖环境刷新阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken) =>
        workspacePreparer.RefreshDependencyEnvironmentAsync(
            context.Layout ?? throw new InvalidOperationException("Runtime layout is required before refreshing dependencies."),
            progressReporter,
            cancellationToken);
}

/// <summary>
/// 在运行时流水线中调用标准 Dome 应用。
/// </summary>
internal sealed class RunDomeStage(
    IDomeApplicationRunner domeApplication,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行 Dome 管线调用阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before running dome.");
        progressReporter.Report("[tr-run] Running dome pipeline...");
        var runResult = await domeApplication.RunAsync(
            new ApplicationAbstractions.RunRequest(layout.SolutionPath, layout.ArtifactsPath, Array.Empty<string>(), TerrariaTools.Dome.Application.Ports.RunMode.Standard),
            cancellationToken);
        context.SetReportPath(runResult.ReportPath ?? Path.Combine(layout.ArtifactsPath, "report.json"));
        if (!runResult.IsSuccess)
        {
            context.TerminalState = new PipelineTerminalState(
                ModelExecution.RunResult.Failure(
                    runResult.FailureCode,
                    runResult.OutputPath,
                    runResult.Message));
        }
    }
}

/// <summary>
/// 从产物目录加载运行报告。
/// </summary>
internal sealed class LoadReportStage(IRunReportStore runReportStore) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行报告加载阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before report load.");
        var reportPath = context.ReportPath ?? Path.Combine(layout.ArtifactsPath, "report.json");
        var reportLoad = await runReportStore.LoadAsync(reportPath, cancellationToken);
        if (!reportLoad.IsSuccess || reportLoad.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Application.Ports.FailureCode)reportLoad.FailureCode, layout.OutputRootPath, reportLoad.Message);
            return;
        }

        if (context.ReportPath == null)
        {
            context.SetReportPath(reportPath);
        }

        context.SetReport(reportLoad.Value);
    }
}

/// <summary>
/// 准备运行时工作区。
/// </summary>
internal sealed class PrepareWorkspaceStage(
    ITerrariaRuntimeWorkspacePreparer workspacePreparer,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行工作区准备阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken) =>
        workspacePreparer.PrepareWorkspaceAsync(
            context.Layout ?? throw new InvalidOperationException("Runtime layout is required before workspace preparation."),
            progressReporter,
            cancellationToken);
}

/// <summary>
/// 构建运行时工作区。
/// </summary>
internal sealed class BuildWorkspaceStage(
    ITerrariaRuntimeBuildExecutor buildExecutor,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行工作区构建阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        context.SetBuildSummary(await buildExecutor.ExecuteAsync(
            context.Layout ?? throw new InvalidOperationException("Runtime layout is required before build."),
            progressReporter,
            cancellationToken));
    }
}

/// <summary>
/// 将运行报告持久化到磁盘。
/// </summary>
internal sealed class PersistReportStage(IRunReportStore runReportStore) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行报告持久化阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var report = context.Report ?? throw new InvalidOperationException("Run report is required before persistence.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before persistence.");
        await runReportStore.SaveAsync(reportPath, context.Report, cancellationToken);
    }
}

/// <summary>
/// 根据构建结果为运行时流水线写入终态。
/// </summary>
internal sealed class FinalizeRuntimeRunStage(ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    /// <summary>
    /// 执行运行时流水线收尾阶段。
    /// </summary>
    /// <param name="context">运行时流水线上下文。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before finalization.");
        var buildSummary = context.BuildSummary ?? throw new InvalidOperationException("Build summary is required before finalization.");
        var report = context.Report ?? throw new InvalidOperationException("Run report is required before finalization.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before finalization.");
        if (!buildSummary.BuildSucceeded)
        {
            SetFailure(
                context,
                TerrariaTools.Dome.Application.Ports.FailureCode.BuildFailed,
                layout.OutputRootPath,
                TerrariaRuntimeStageHelpers.BuildFailureMessage(buildSummary, report.AdvancedAnalysisSummary));
            return Task.CompletedTask;
        }

        progressReporter.Report("[tr-run] Runtime pipeline completed.");
        context.TerminalState = new PipelineTerminalState(ModelExecution.RunResult.Success(layout.OutputRootPath, reportPath));
        return Task.CompletedTask;
    }
}

/// <summary>
/// 提供运行时流水线共享的辅助逻辑。
/// </summary>
internal static class TerrariaRuntimeStageHelpers
{
    /// <summary>
    /// 为构建失败生成附带高级分析信息的消息。
    /// </summary>
    /// <param name="buildSummary">运行时构建摘要。</param>
    /// <param name="advancedAnalysisSummary">高级分析摘要。</param>
    /// <returns>组合后的失败消息。</returns>
    public static string BuildFailureMessage(
        ApplicationAbstractions.TerrariaRuntimeBuildSummary buildSummary,
        CoreAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary)
    {
        if (advancedAnalysisSummary == null)
        {
            return buildSummary.StandardError;
        }

        var notes = string.Join(", ", (advancedAnalysisSummary.Notes ?? Array.Empty<string>()).Take(3));
        var suffix = $"Advanced analysis: persistent types={advancedAnalysisSummary.PersistentTypeCount}, risky types={advancedAnalysisSummary.RiskyTypeCount}.";
        if (!string.IsNullOrEmpty(notes))
        {
            suffix += $" Notes: {notes}.";
        }

        return string.IsNullOrWhiteSpace(buildSummary.StandardError)
            ? suffix
            : $"{buildSummary.StandardError}{Environment.NewLine}{suffix}";
    }
}

internal sealed class TerrariaRuntimePipelineFlowExecutionContext : ApplicationAbstractions.IFlowExecutionContext
{
    private readonly Dictionary<string, object?> items = new(StringComparer.Ordinal);

    internal TerrariaRuntimePipelineFlowExecutionContext(string correlationId)
    {
        CorrelationId = correlationId;
    }

    public string CorrelationId { get; }

    public IDictionary<string, object?> Items => items;
}

internal static class TerrariaRuntimePipelineFlowExecutionContexts
{
    private static readonly ConditionalWeakTable<TerrariaRuntimePipelineContext, TerrariaRuntimePipelineFlowExecutionContext> Contexts = new();

    public static ApplicationAbstractions.IFlowExecutionContext GetOrCreate(TerrariaRuntimePipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Contexts.GetValue(
            context,
            static _ => new TerrariaRuntimePipelineFlowExecutionContext(Guid.NewGuid().ToString("n")));
    }
}

internal sealed class PrepareRuntimeSlotStage(ITerrariaRuntimePrepareSlot slot) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var output = await slot.ExecuteAsync(
            new TerrariaRuntimePrepareInput(context.Request),
            TerrariaRuntimePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        context.SetLayout(output.Layout);
    }
}

internal sealed class ExecuteDomeRuntimeSlotStage(ITerrariaRuntimeExecuteDomeSlot slot) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before running dome.");
        var output = await slot.ExecuteAsync(
            new TerrariaRuntimeExecuteDomeInput(new TerrariaRuntimePrepareOutput(context.Request, layout)),
            TerrariaRuntimePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        context.SetReportPath(output.ReportPath);
        if (output.FailureResult != null)
        {
            context.TerminalState = new PipelineTerminalState(output.FailureResult);
        }
    }
}

internal sealed class LoadReportRuntimeSlotStage(ITerrariaRuntimeLoadReportSlot slot) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before report load.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before report load.");
        var reportLoad = await slot.ExecuteAsync(
            new TerrariaRuntimeLoadReportInput(
                new TerrariaRuntimeExecuteDomeOutput(
                    new TerrariaRuntimePrepareOutput(context.Request, layout),
                    reportPath,
                    null)),
            TerrariaRuntimePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        if (!reportLoad.IsSuccess || reportLoad.Value == null)
        {
            SetFailure(
                context,
                reportLoad.FailureCode,
                layout.OutputRootPath,
                reportLoad.Message);
            return;
        }

        context.SetReport(reportLoad.Value.Report);
    }
}

internal sealed class BuildWorkspaceRuntimeSlotStage(ITerrariaRuntimeBuildWorkspaceSlot slot) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before build.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before build.");
        var report = context.Report ?? throw new InvalidOperationException("Run report is required before build.");
        var output = await slot.ExecuteAsync(
            new TerrariaRuntimeBuildWorkspaceInput(
                new TerrariaRuntimeLoadReportOutput(
                    new TerrariaRuntimeExecuteDomeOutput(
                        new TerrariaRuntimePrepareOutput(context.Request, layout),
                        reportPath,
                        null),
                    report)),
            TerrariaRuntimePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        context.SetBuildSummary(output.BuildSummary);
    }
}

internal sealed class PersistRuntimeSlotStage(ITerrariaRuntimePersistSlot slot) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before persistence.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before persistence.");
        var report = context.Report ?? throw new InvalidOperationException("Run report is required before persistence.");
        var buildSummary = context.BuildSummary ?? throw new InvalidOperationException("Build summary is required before persistence.");
        var result = await slot.ExecuteAsync(
            new TerrariaRuntimePersistInput(
                new TerrariaRuntimeBuildWorkspaceOutput(
                    new TerrariaRuntimeLoadReportOutput(
                        new TerrariaRuntimeExecuteDomeOutput(
                            new TerrariaRuntimePrepareOutput(context.Request, layout),
                            reportPath,
                            null),
                        report),
                    buildSummary)),
            TerrariaRuntimePipelineFlowExecutionContexts.GetOrCreate(context),
            cancellationToken);
        context.TerminalState = new PipelineTerminalState(result);
    }
}
