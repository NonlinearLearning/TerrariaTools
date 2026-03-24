namespace TerrariaTools.Dome.Application.UseCases.ShadowExtraction;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// 承载影子提取流水线在各阶段之间流转的状态。
/// </summary>
internal sealed class ShadowExtractionPipelineContext : PipelineContextBase
{
    /// <summary>
    /// 初始化影子提取流水线上下文。
    /// </summary>
    /// <param name="request">影子提取请求。</param>
    public ShadowExtractionPipelineContext(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request)
    {
        Request = request;
        OutputRootPath = new TerrariaRuntimeShadowLayoutFactory().Create(request).OutputRootPath;
    }

    /// <summary>
    /// 获取影子提取请求。
    /// </summary>
    public ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest Request { get; }

    /// <summary>
    /// 获取预估输出根目录。
    /// </summary>
    public string OutputRootPath { get; }

    /// <summary>
    /// 获取输入解析结果。
    /// </summary>
    public ShadowExtractionInputResolution? InputResolution { get; private set; }

    /// <summary>
    /// 获取分析结果。
    /// </summary>
    public ShadowExtractionAnalysis? Analysis { get; private set; }

    /// <summary>
    /// 获取闭包计划。
    /// </summary>
    public ShadowClosurePlan? ClosurePlan { get; private set; }

    /// <summary>
    /// 获取工作区写入结果。
    /// </summary>
    public ShadowWorkspaceWriteResult? WorkspaceWriteResult { get; private set; }

    /// <summary>
    /// 获取影子提取报告。
    /// </summary>
    public ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport? Report { get; private set; }

    /// <summary>
    /// 获取构建摘要。
    /// </summary>
    public ApplicationAbstractions.TerrariaRuntimeBuildSummary? BuildSummary { get; private set; }

    /// <summary>
    /// 获取报告路径。
    /// </summary>
    public string? ReportPath { get; private set; }

    /// <summary>
    /// 写入输入解析结果。
    /// </summary>
    /// <param name="inputResolution">输入解析结果。</param>
    internal void SetInputResolution(ShadowExtractionInputResolution inputResolution)
    {
        EnsureCanMutate("set input resolution");
        InputResolution = InputResolution == null ? inputResolution : throw new InvalidOperationException("Input resolution is already set.");
    }

    /// <summary>
    /// 写入分析结果。
    /// </summary>
    /// <param name="analysis">分析结果。</param>
    internal void SetAnalysis(ShadowExtractionAnalysis analysis)
    {
        EnsureCanMutate("set analysis");
        Analysis = Analysis == null ? analysis : throw new InvalidOperationException("Analysis is already set.");
    }

    /// <summary>
    /// 写入闭包计划。
    /// </summary>
    /// <param name="closurePlan">闭包计划。</param>
    internal void SetClosurePlan(ShadowClosurePlan closurePlan)
    {
        EnsureCanMutate("set closure plan");
        ClosurePlan = ClosurePlan == null ? closurePlan : throw new InvalidOperationException("Closure plan is already set.");
    }

    /// <summary>
    /// 写入工作区写入结果。
    /// </summary>
    /// <param name="workspaceWriteResult">工作区写入结果。</param>
    internal void SetWorkspaceWriteResult(ShadowWorkspaceWriteResult workspaceWriteResult)
    {
        EnsureCanMutate("set workspace write result");
        WorkspaceWriteResult = WorkspaceWriteResult == null ? workspaceWriteResult : throw new InvalidOperationException("Workspace write result is already set.");
    }

    /// <summary>
    /// 写入影子提取报告。
    /// </summary>
    /// <param name="report">影子提取报告。</param>
    internal void SetReport(ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report)
    {
        EnsureCanMutate("set report");
        Report = Report == null ? report : throw new InvalidOperationException("Report is already set.");
    }

    /// <summary>
    /// 用最新版本替换当前影子提取报告。
    /// </summary>
    /// <param name="report">新的影子提取报告。</param>
    internal void UpdateReport(ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report)
    {
        EnsureCanMutate("update report");
        Report = report;
    }

    /// <summary>
    /// 写入构建摘要。
    /// </summary>
    /// <param name="buildSummary">构建摘要。</param>
    internal void SetBuildSummary(ApplicationAbstractions.TerrariaRuntimeBuildSummary buildSummary)
    {
        EnsureCanMutate("set build summary");
        BuildSummary = BuildSummary == null ? buildSummary : throw new InvalidOperationException("Build summary is already set.");
    }

    /// <summary>
    /// 写入报告路径。
    /// </summary>
    /// <param name="reportPath">报告路径。</param>
    internal void SetReportPath(string reportPath)
    {
        EnsureCanMutate("set report path");
        ReportPath = ReportPath == null ? reportPath : throw new InvalidOperationException("Report path is already set.");
    }
}
