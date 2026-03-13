namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 不可变分析事实和查询服务的兼容性外观。
/// </summary>
public sealed class AnalysisContext
{
    private AnalysisContext(AnalysisSnapshot snapshot, AnalysisServices services)
    {
        Snapshot = snapshot;
        Services = services;
    }

    /// <summary>
    /// 分析快照。
    /// </summary>
    public AnalysisSnapshot Snapshot { get; }

    /// <summary>
    /// 分析服务集合。
    /// </summary>
    public AnalysisServices Services { get; }

    /// <summary>
    /// 分析视图。
    /// </summary>
    public AnalysisView View => Snapshot.View;

    /// <summary>
    /// 继承关系查询服务。
    /// </summary>
    public IInheritanceQueryService Inheritance => Services.Inheritance;

    /// <summary>
    /// 引用关系查询服务。
    /// </summary>
    public IReferenceQueryService References => Services.References;

    /// <summary>
    /// 函数索引。
    /// </summary>
    public FunctionIndex FunctionIndex => Snapshot.FunctionIndex;

    /// <summary>
    /// 函数事实索引。
    /// </summary>
    public FunctionFactsIndex FunctionFacts => Snapshot.FunctionFacts;

    /// <summary>
    /// 语句事实索引。
    /// </summary>
    public StatementFactsIndex StatementFacts => Snapshot.StatementFacts;

    /// <summary>
    /// 语句分析服务。
    /// </summary>
    public IStatementAnalysisService Statements => Services.Statements;

    /// <summary>
    /// 函数图提供器。
    /// </summary>
    public IFunctionGraphProvider FunctionGraphs => Services.FunctionGraphs;

    /// <summary>
    /// 创建分析上下文。
    /// </summary>
    /// <param name="snapshot">分析快照。</param>
    /// <param name="services">分析服务。</param>
    /// <returns>分析上下文实例。</returns>
    public static AnalysisContext Create(AnalysisSnapshot snapshot, AnalysisServices services) =>
        new(snapshot, services);
}
