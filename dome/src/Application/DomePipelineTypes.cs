namespace TerrariaTools.Dome.Application;

using System.Diagnostics;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Analysis.Roslyn;

/// <summary>
/// Dome 流水线上下文。
/// </summary>
public sealed class DomePipelineContext : PipelineContextBase
{
    /// <summary>
    /// 初始化 Dome 流水线上下文。
    /// </summary>
    /// <param name="request">运行请求。</param>
    public DomePipelineContext(ApplicationAbstractions.RunRequest request)
    {
        Request = request;
    }

    public ApplicationAbstractions.RunRequest Request { get; }

    internal Stopwatch RunStopwatch { get; } = Stopwatch.StartNew();

    internal ApplicationAbstractions.WorkspaceLoadResult? LoadResult { get; private set; }

    internal ApplicationAbstractions.AnalysisEngineResult? AnalysisResult { get; private set; }

    internal DomeAnalyzedWorkspace? AnalyzedWorkspace { get; private set; }

    internal DomeDecisionSet? Decisions { get; private set; }

    internal ModelPlanning.PlanCompilationResult? PlanResult { get; private set; }

    internal ModelPlanning.FunctionImpactSet? FunctionImpactSet { get; private set; }

    internal DomeRewriteOutcome? RewriteOutcome { get; private set; }

    /// <summary>
    /// 设置工作区加载结果。
    /// </summary>
    /// <param name="loadResult">工作区加载结果。</param>
    internal void SetLoadResult(
        ApplicationAbstractions.WorkspaceLoadResult loadResult)
    {
        EnsureCanMutate("set load result");
        if (LoadResult != null)
        {
            throw new InvalidOperationException("Load result is already set.");
        }

        LoadResult = loadResult;
    }

    /// <summary>
    /// 设置分析结果与分析上下文。
    /// </summary>
    /// <param name="analysisResult">分析结果。</param>
    /// <param name="analyzedWorkspace">分析工作区。</param>
    internal void SetAnalysisResult(
        ApplicationAbstractions.AnalysisEngineResult analysisResult,
        DomeAnalyzedWorkspace analyzedWorkspace)
    {
        EnsureCanMutate("set analysis result");
        if (AnalysisResult != null || AnalyzedWorkspace != null)
        {
            throw new InvalidOperationException("Analysis result is already set.");
        }

        AnalysisResult = analysisResult;
        AnalyzedWorkspace = analyzedWorkspace;
    }

    /// <summary>
    /// 设置标记决策集合。
    /// </summary>
    /// <param name="decisions">标记决策集合。</param>
    internal void SetDecisions(DomeDecisionSet decisions)
    {
        EnsureCanMutate("set decisions");
        Decisions = Decisions == null ? decisions : throw new InvalidOperationException("Decisions are already set.");
    }

    /// <summary>
    /// 设置计划编译结果。
    /// </summary>
    /// <param name="planResult">计划编译结果。</param>
    internal void SetPlanResult(ModelPlanning.PlanCompilationResult planResult)
    {
        EnsureCanMutate("set plan result");
        PlanResult = PlanResult == null ? planResult : throw new InvalidOperationException("Plan result is already set.");
    }

    /// <summary>
    /// 设置函数影响集合。
    /// </summary>
    /// <param name="functionImpactSet">函数影响集合。</param>
    internal void SetFunctionImpactSet(ModelPlanning.FunctionImpactSet? functionImpactSet)
    {
        EnsureCanMutate("set function impact summary");
        FunctionImpactSet = functionImpactSet;
    }

    /// <summary>
    /// 设置重写结果。
    /// </summary>
    /// <param name="rewriteOutcome">重写结果。</param>
    internal void SetRewriteOutcome(DomeRewriteOutcome rewriteOutcome)
    {
        EnsureCanMutate("set rewrite outcome");
        RewriteOutcome = RewriteOutcome == null ? rewriteOutcome : throw new InvalidOperationException("Rewrite outcome is already set.");
    }
}

/// <summary>
/// Dome 分析后的工作区数据。
/// </summary>
internal sealed record DomeAnalyzedWorkspace(
    ModelAnalysis.AnalysisContext Context,
    ModelAnalysis.AdvancedAnalysisSummary AdvancedAnalysisSummary);

/// <summary>
/// Dome 决策集合。
/// </summary>
internal sealed record DomeDecisionSet(
    IReadOnlyList<ModelRules.MarkDecision> InitialDecisions,
    IReadOnlyList<ModelRules.MarkDecision> PredictedDecisions,
    IReadOnlyList<ModelRules.MarkDecision> AllDecisions);

/// <summary>
/// Dome 重写结果。
/// </summary>
internal sealed record DomeRewriteOutcome(
    IReadOnlyList<string> RewrittenDocuments,
    string? FailureMessage);
