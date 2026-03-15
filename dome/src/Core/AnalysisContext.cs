namespace TerrariaTools.Dome.Core;

/// <summary>
/// 分析上下文，聚合分析快照与查询服务。
/// </summary>
public sealed class AnalysisContext
{
    /// <summary>
    /// 初始化分析上下文。
    /// </summary>
    /// <param name="snapshot">分析执行快照。</param>
    /// <param name="services">分析服务集合。</param>
    private AnalysisContext(AnalysisExecutionSnapshot snapshot, AnalysisServices services)
    {
        Snapshot = snapshot;
        Services = services;
    }

    /// <summary>
    /// 获取分析执行快照。
    /// </summary>
    public AnalysisExecutionSnapshot Snapshot { get; }

    /// <summary>
    /// 获取分析服务集合。
    /// </summary>
    public AnalysisServices Services { get; }

    /// <summary>
    /// 获取分析结果视图。
    /// </summary>
    public AnalysisResultModel View => Snapshot.View;

    /// <summary>
    /// 获取继承关系查询服务。
    /// </summary>
    public IInheritanceQueryService Inheritance => Services.Inheritance;

    /// <summary>
    /// 获取引用关系查询服务。
    /// </summary>
    public IReferenceQueryService References => Services.References;

    /// <summary>
    /// 获取函数索引。
    /// </summary>
    public FunctionIndex FunctionIndex => Snapshot.FunctionIndex;

    /// <summary>
    /// 获取函数事实索引。
    /// </summary>
    public FunctionFactsIndex FunctionFacts => Snapshot.FunctionFacts;

    /// <summary>
    /// 获取语句事实索引。
    /// </summary>
    public StatementFactsIndex StatementFacts => Snapshot.StatementFacts;

    /// <summary>
    /// 获取语句分析服务。
    /// </summary>
    public IStatementAnalysisService Statements => Services.Statements;

    /// <summary>
    /// 获取函数依赖图服务。
    /// </summary>
    public IFunctionGraphProvider FunctionGraphs => Services.FunctionGraphs;

    /// <summary>
    /// 获取符号依赖图服务。
    /// </summary>
    public ISymbolDependencyGraphProvider SymbolDependencies => Services.SymbolDependencies;

    /// <summary>
    /// 获取方法调用查询服务。
    /// </summary>
    public IMethodCallQueryService MethodCalls => Services.MethodCalls;

    /// <summary>
    /// 获取数据流摘要服务。
    /// </summary>
    public IDataFlowSummaryService DataFlow => Services.DataFlow;

    /// <summary>
    /// 获取分支流摘要服务。
    /// </summary>
    public ISwitchFlowSummaryService SwitchFlows => Services.SwitchFlows;

    /// <summary>
    /// 获取调用链分析服务。
    /// </summary>
    public ICallChainAnalysisService CallChains => Services.CallChains;

    /// <summary>
    /// 获取高级分析摘要服务。
    /// </summary>
    public IAdvancedAnalysisSummaryService AdvancedAnalysis => Services.AdvancedAnalysis;

    /// <summary>
    /// 获取成员清理查询服务。
    /// </summary>
    public IMemberCleanupQueryService MemberCleanup => Services.MemberCleanup;

    /// <summary>
    /// 创建分析上下文实例。
    /// </summary>
    /// <param name="snapshot">分析执行快照。</param>
    /// <param name="services">分析服务集合。</param>
    /// <returns>分析上下文实例。</returns>
    public static AnalysisContext Create(AnalysisExecutionSnapshot snapshot, AnalysisServices services) =>
        new(snapshot, services);
}
