namespace TerrariaTools.Dome.Application.Pipeline;

using System.Diagnostics;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 承载标准 Dome 流水线在各阶段之间流转的可变状态。
/// </summary>
public sealed class DomePipelineContext : PipelineContextBase
{
    /// <summary>
    /// 初始化标准 Dome 流水线上下文。
    /// </summary>
    /// <param name="request">本次运行请求。</param>
    public DomePipelineContext(ApplicationAbstractions.RunRequest request)
    {
        Request = request;
    }

    /// <summary>
    /// 获取本次运行请求。
    /// </summary>
    public ApplicationAbstractions.RunRequest Request { get; }

    /// <summary>
    /// 获取整条流水线的运行计时器。
    /// </summary>
    internal Stopwatch RunStopwatch { get; } = Stopwatch.StartNew();

    /// <summary>
    /// 获取工作区加载结果。
    /// </summary>
    internal ApplicationAbstractions.WorkspaceLoadResult? LoadResult { get; private set; }

    /// <summary>
    /// 获取分析输出。
    /// </summary>
    internal CoreAnalysis.AnalysisOutput? AnalysisOutput { get; private set; }

    /// <summary>
    /// 获取当前构建的决策集合。
    /// </summary>
    internal CoreRules.DecisionSet? Decisions { get; private set; }

    /// <summary>
    /// 获取计划编译输出。
    /// </summary>
    internal CorePlanning.PlanningOutput? PlanningOutput { get; private set; }

    /// <summary>
    /// 获取重写输出。
    /// </summary>
    internal ModelExecution.RewriteOutput? RewriteOutput { get; private set; }

    /// <summary>
    /// 写入工作区加载结果。
    /// </summary>
    /// <param name="loadResult">工作区加载结果。</param>
    internal void SetLoadResult(ApplicationAbstractions.WorkspaceLoadResult loadResult)
    {
        EnsureCanMutate("set load result");
        LoadResult = LoadResult == null ? loadResult : throw new InvalidOperationException("Load result is already set.");
    }

    /// <summary>
    /// 写入分析输出。
    /// </summary>
    /// <param name="analysisOutput">分析输出。</param>
    internal void SetAnalysisOutput(CoreAnalysis.AnalysisOutput analysisOutput)
    {
        EnsureCanMutate("set analysis output");
        AnalysisOutput = AnalysisOutput == null ? analysisOutput : throw new InvalidOperationException("Analysis output is already set.");
    }

    /// <summary>
    /// 写入标记决策集合。
    /// </summary>
    /// <param name="decisions">标记决策集合。</param>
    internal void SetDecisions(CoreRules.DecisionSet decisions)
    {
        EnsureCanMutate("set decisions");
        Decisions = Decisions == null ? decisions : throw new InvalidOperationException("Decisions are already set.");
    }

    /// <summary>
    /// 写入计划编译输出。
    /// </summary>
    /// <param name="planningOutput">计划编译输出。</param>
    internal void SetPlanningOutput(CorePlanning.PlanningOutput planningOutput)
    {
        EnsureCanMutate("set planning output");
        PlanningOutput = PlanningOutput == null ? planningOutput : throw new InvalidOperationException("Planning output is already set.");
    }

    /// <summary>
    /// 写入重写输出。
    /// </summary>
    /// <param name="rewriteOutput">重写输出。</param>
    internal void SetRewriteOutput(ModelExecution.RewriteOutput rewriteOutput)
    {
        EnsureCanMutate("set rewrite output");
        RewriteOutput = RewriteOutput == null ? rewriteOutput : throw new InvalidOperationException("Rewrite output is already set.");
    }
}
