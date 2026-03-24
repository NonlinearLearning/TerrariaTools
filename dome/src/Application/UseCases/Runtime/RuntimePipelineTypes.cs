namespace TerrariaTools.Dome.Application.UseCases.Runtime;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;
using ModelExecution = TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 承载运行时流水线在各阶段之间流转的状态。
/// </summary>
internal sealed class TerrariaRuntimePipelineContext : PipelineContextBase
{
    /// <summary>
    /// 初始化运行时流水线上下文。
    /// </summary>
    /// <param name="request">运行时请求。</param>
    public TerrariaRuntimePipelineContext(ApplicationAbstractions.TerrariaRuntimeRunRequest request)
    {
        Request = request;
    }

    /// <summary>
    /// 获取运行时请求。
    /// </summary>
    public ApplicationAbstractions.TerrariaRuntimeRunRequest Request { get; }

    /// <summary>
    /// 获取运行时工作区布局。
    /// </summary>
    public ApplicationAbstractions.TerrariaRuntimeLayout? Layout { get; private set; }

    /// <summary>
    /// 获取运行报告路径。
    /// </summary>
    public string? ReportPath { get; private set; }

    /// <summary>
    /// 获取当前运行报告。
    /// </summary>
    public ModelExecution.RunReport? Report { get; private set; }

    /// <summary>
    /// 获取运行时构建摘要。
    /// </summary>
    public ApplicationAbstractions.TerrariaRuntimeBuildSummary? BuildSummary { get; private set; }

    /// <summary>
    /// 写入运行时工作区布局。
    /// </summary>
    /// <param name="layout">运行时工作区布局。</param>
    internal void SetLayout(ApplicationAbstractions.TerrariaRuntimeLayout layout)
    {
        EnsureCanMutate("set runtime layout");
        Layout = Layout == null ? layout : throw new InvalidOperationException("Runtime layout is already set.");
    }

    /// <summary>
    /// 写入运行报告路径。
    /// </summary>
    /// <param name="reportPath">运行报告路径。</param>
    internal void SetReportPath(string reportPath)
    {
        EnsureCanMutate("set report path");
        ReportPath = ReportPath == null ? reportPath : throw new InvalidOperationException("Report path is already set.");
    }

    /// <summary>
    /// 写入运行报告。
    /// </summary>
    /// <param name="report">运行报告。</param>
    internal void SetReport(ModelExecution.RunReport report)
    {
        EnsureCanMutate("set report");
        Report = Report == null ? report : throw new InvalidOperationException("Report is already set.");
    }

    /// <summary>
    /// 用最新版本替换当前运行报告。
    /// </summary>
    /// <param name="report">新的运行报告。</param>
    internal void UpdateReport(ModelExecution.RunReport report)
    {
        EnsureCanMutate("update report");
        Report = report;
    }

    /// <summary>
    /// 写入运行时构建摘要。
    /// </summary>
    /// <param name="buildSummary">运行时构建摘要。</param>
    internal void SetBuildSummary(ApplicationAbstractions.TerrariaRuntimeBuildSummary buildSummary)
    {
        EnsureCanMutate("set build summary");
        BuildSummary = BuildSummary == null ? buildSummary : throw new InvalidOperationException("Build summary is already set.");
    }
}
