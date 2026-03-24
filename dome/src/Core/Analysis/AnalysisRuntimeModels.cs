using TerrariaTools.Dome.Core.Common;
using TerrariaTools.Dome.Core.Cpg;

namespace TerrariaTools.Dome.Core.Analysis;

/// <summary>
/// 表示分析执行产生的快照。
/// </summary>
public sealed record AnalysisExecutionSnapshot(
    AnalysisResultModel View,
    FunctionIndex FunctionIndex,
    FunctionFactsIndex FunctionFacts,
    StatementFactsIndex StatementFacts,
    DomeCpg CodePropertyGraph);

/// <summary>
/// 表示分析阶段的性能摘要。
/// </summary>
public sealed record AnalysisPerformanceSummary(int DocumentCount, TimeSpan SyntaxIndexTime, TimeSpan TypeGraphTime, TimeSpan FunctionNodeTime, TimeSpan TypeBodyGraphTime, TimeSpan TargetAnalysisTime, TimeSpan FunctionFactsTime, TimeSpan MergeTime);

/// <summary>
/// 表示分析运行时依赖的服务集合。
/// </summary>
public sealed record AnalysisServices(IInheritanceQueryService Inheritance, IReferenceQueryService References, IStatementAnalysisService Statements, IFunctionGraphProvider FunctionGraphs, ISymbolDependencyGraphProvider SymbolDependencies, IMethodCallQueryService MethodCalls, IDataFlowSummaryService DataFlow, ISwitchFlowSummaryService SwitchFlows, ICallChainAnalysisService CallChains, IAdvancedAnalysisSummaryService AdvancedAnalysis, IMemberCleanupQueryService MemberCleanup);

/// <summary>
/// 提供对分析快照及相关服务的统一访问入口。
/// </summary>
public sealed class AnalysisContext
{
    /// <summary>
    /// 初始化分析上下文。
    /// </summary>
    private AnalysisContext(AnalysisExecutionSnapshot snapshot, AnalysisServices services)
    {
        Snapshot = snapshot;
        Services = services;
    }

    public AnalysisExecutionSnapshot Snapshot { get; }

    public AnalysisServices Services { get; }

    public AnalysisResultModel View => Snapshot.View;

    public FunctionIndex FunctionIndex => Snapshot.FunctionIndex;

    public FunctionFactsIndex FunctionFacts => Snapshot.FunctionFacts;

    public StatementFactsIndex StatementFacts => Snapshot.StatementFacts;

    public DomeCpg CodePropertyGraph => Snapshot.CodePropertyGraph;

    public IInheritanceQueryService Inheritance => Services.Inheritance;

    public IReferenceQueryService References => Services.References;

    public IStatementAnalysisService Statements => Services.Statements;

    public IFunctionGraphProvider FunctionGraphs => Services.FunctionGraphs;

    public ISymbolDependencyGraphProvider SymbolDependencies => Services.SymbolDependencies;

    public IMethodCallQueryService MethodCalls => Services.MethodCalls;

    public IDataFlowSummaryService DataFlow => Services.DataFlow;

    public ISwitchFlowSummaryService SwitchFlows => Services.SwitchFlows;

    public ICallChainAnalysisService CallChains => Services.CallChains;

    public IAdvancedAnalysisSummaryService AdvancedAnalysis => Services.AdvancedAnalysis;

    public IMemberCleanupQueryService MemberCleanup => Services.MemberCleanup;

    /// <summary>
    /// 创建分析上下文实例。
    /// </summary>
    public static AnalysisContext Create(AnalysisExecutionSnapshot snapshot, AnalysisServices services) => new(snapshot, services);
}

/// <summary>
/// 表示分析阶段的完整输出。
/// </summary>
public sealed record AnalysisOutput(
    AnalysisResultModel View,
    AnalysisExecutionSnapshot Snapshot,
    AnalysisServices Services,
    AnalysisPerformanceSummary PerformanceSummary)
{
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

    public DomeCpg CodePropertyGraph => Snapshot.CodePropertyGraph;

    /// <summary>
    /// 从当前输出创建分析上下文。
    /// </summary>
    public AnalysisContext CreateContext() => AnalysisContext.Create(Snapshot, Services);
}

/// <summary>
/// 定义继承关系查询服务。
/// </summary>
public interface IInheritanceQueryService
{
    /// <summary>
    /// 判断成员是否重写了基类成员。
    /// </summary>
    bool IsOverrideMember(string memberId);

    /// <summary>
    /// 判断成员是否实现了接口成员。
    /// </summary>
    bool ImplementsInterfaceMember(string memberId);

    /// <summary>
    /// 判断类型是否位于继承链中。
    /// </summary>
    bool IsInInheritanceChain(string typeId);
}

/// <summary>
/// 定义引用关系查询服务。
/// </summary>
public interface IReferenceQueryService
{
    /// <summary>
    /// 判断符号或成员是否存在引用。
    /// </summary>
    bool HasReferences(string symbolOrMemberId);

    /// <summary>
    /// 获取引用指定符号或成员的方法集合。
    /// </summary>
    IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId);

    /// <summary>
    /// 获取引用指定符号或成员的类型集合。
    /// </summary>
    IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId);
}

/// <summary>
/// 定义函数图提供服务。
/// </summary>
public interface IFunctionGraphProvider
{
    /// <summary>
    /// 获取指定请求对应的函数图快照。
    /// </summary>
    FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request);
}

/// <summary>
/// 定义符号依赖图提供服务。
/// </summary>
public interface ISymbolDependencyGraphProvider
{
    /// <summary>
    /// 获取从根符号出发的正向依赖切片。
    /// </summary>
    SymbolDependencySlice GetForwardSlice(IReadOnlyList<string> rootSymbolIds, SymbolDependencyQueryOptions options);
}

/// <summary>
/// 定义方法可达性查询服务。
/// </summary>
public interface IMethodCallQueryService
{
    /// <summary>
    /// 获取从根成员出发可达的方法集合。
    /// </summary>
    IReadOnlyList<MemberId> GetReachableMethods(IReadOnlyList<MemberId> rootMemberIds);
}

/// <summary>
/// 定义数据流摘要服务。
/// </summary>
public interface IDataFlowSummaryService
{
    /// <summary>
    /// 分析指定目标并返回数据流摘要。
    /// </summary>
    DataFlowSummary Analyze(string targetKey);
}

/// <summary>
/// 定义 switch 流程摘要服务。
/// </summary>
public interface ISwitchFlowSummaryService
{
    /// <summary>
    /// 分析指定目标并返回 switch 流程摘要。
    /// </summary>
    SwitchFlowSummary Analyze(string targetKey);
}

/// <summary>
/// 定义调用链分析服务。
/// </summary>
public interface ICallChainAnalysisService
{
    /// <summary>
    /// 分析指定成员并返回调用链摘要。
    /// </summary>
    CallChainAnalysisSummary Analyze(string memberId);
}

/// <summary>
/// 定义高级分析摘要服务。
/// </summary>
public interface IAdvancedAnalysisSummaryService
{
    /// <summary>
    /// 构建高级分析摘要。
    /// </summary>
    AdvancedAnalysisSummary BuildSummary();
}

/// <summary>
/// 定义成员清理查询服务。
/// </summary>
public interface IMemberCleanupQueryService
{
    /// <summary>
    /// 获取指定符号的清理信息。
    /// </summary>
    MemberCleanupSymbolInfo? GetSymbolInfo(string symbolId);

    /// <summary>
    /// 获取指定类型的清理信息。
    /// </summary>
    MemberCleanupTypeInfo? GetTypeInfo(string typeId);

    /// <summary>
    /// 判断符号是否存在任何引用。
    /// </summary>
    bool HasAnyReferences(string symbolId);

    /// <summary>
    /// 判断方法是否存在内部引用。
    /// </summary>
    bool HasInternalMethodReferences(MemberId memberId);

    /// <summary>
    /// 判断方法是否存在外部引用。
    /// </summary>
    bool HasExternalMethodReferences(MemberId memberId);

    /// <summary>
    /// 获取可重排的公共方法集合。
    /// </summary>
    IReadOnlyList<MemberId> GetReorderablePublicMethods(string typeId);
}

/// <summary>
/// 定义语句分析服务。
/// </summary>
public interface IStatementAnalysisService
{
    /// <summary>
    /// 使用默认作用域分析指定目标。
    /// </summary>
    StatementGraphSnapshot Analyze(string targetKey);

    /// <summary>
    /// 使用指定作用域分析指定目标。
    /// </summary>
    StatementGraphSnapshot Analyze(string targetKey, StatementScopeMode scopeMode);
}

/// <summary>
/// 表示函数图快照。
/// </summary>
public sealed record FunctionGraphSnapshot(FunctionGraphScope Scope, IReadOnlyList<MemberId> RootMemberIds, IReadOnlyList<string> IncludedDocumentPaths, FunctionDependencyGraph Graph);

/// <summary>
/// 表示函数图作用域。
/// </summary>
public enum FunctionGraphScope
{
    /// <summary>
    /// 整个项目范围。
    /// </summary>
    WholeProject,

    /// <summary>
    /// 根成员展开范围。
    /// </summary>
    ExpandedMembers
}

/// <summary>
/// 表示函数图请求。
/// </summary>
public sealed record FunctionGraphRequest(FunctionGraphScope Scope, IReadOnlyList<MemberId> RootMemberIds, int Depth, IReadOnlyList<FunctionDependencyKind> EdgeKinds, string Requester, string Reason);

/// <summary>
/// 表示符号依赖图节点。
/// </summary>
public sealed record SymbolDependencyNode(string SymbolId, string DisplayName, SymbolDependencyNodeKind Kind, string? DocumentPath);

/// <summary>
/// 表示符号依赖图边。
/// </summary>
public sealed record SymbolDependencyEdge(string SourceSymbolId, string TargetSymbolId, SymbolDependencyEdgeKind Kind);

/// <summary>
/// 表示完整符号依赖图。
/// </summary>
public sealed record SymbolDependencyGraph(IReadOnlyList<SymbolDependencyNode> Nodes, IReadOnlyList<SymbolDependencyEdge> Edges);

/// <summary>
/// 表示符号依赖查询选项。
/// </summary>
public sealed record SymbolDependencyQueryOptions(IReadOnlyList<SymbolDependencyEdgeKind> AllowedEdgeKinds, IReadOnlyList<SymbolDependencyNodeKind> AllowedNodeKinds);

/// <summary>
/// 表示符号依赖路径。
/// </summary>
public sealed record SymbolDependencyPath(IReadOnlyList<string> SymbolIds, IReadOnlyList<SymbolDependencyEdgeKind> EdgeKinds);

/// <summary>
/// 表示符号依赖切片结果。
/// </summary>
public sealed record SymbolDependencySlice(IReadOnlyList<SymbolDependencyNode> Nodes, IReadOnlyList<SymbolDependencyEdge> Edges, IReadOnlyList<SymbolDependencyPath> Paths);

/// <summary>
/// 表示符号依赖节点类型。
/// </summary>
public enum SymbolDependencyNodeKind
{
    /// <summary>
    /// 类型节点。
    /// </summary>
    Type,

    /// <summary>
    /// 方法节点。
    /// </summary>
    Method,

    /// <summary>
    /// 属性节点。
    /// </summary>
    Property,

    /// <summary>
    /// 字段节点。
    /// </summary>
    Field,

    /// <summary>
    /// 事件节点。
    /// </summary>
    Event
}

/// <summary>
/// 表示符号依赖边类型。
/// </summary>
public enum SymbolDependencyEdgeKind
{
    /// <summary>
    /// 包含类型边。
    /// </summary>
    ContainsType,

    /// <summary>
    /// 基类依赖边。
    /// </summary>
    BaseType,

    /// <summary>
    /// 接口实现边。
    /// </summary>
    InterfaceImplementation,

    /// <summary>
    /// 显式接口实现边。
    /// </summary>
    ExplicitInterfaceImplementation,

    /// <summary>
    /// 重写依赖边。
    /// </summary>
    Override,

    /// <summary>
    /// 返回类型依赖边。
    /// </summary>
    ReturnType,

    /// <summary>
    /// 参数类型依赖边。
    /// </summary>
    ParameterType,

    /// <summary>
    /// 字段类型依赖边。
    /// </summary>
    FieldType,

    /// <summary>
    /// 属性类型依赖边。
    /// </summary>
    PropertyType,

    /// <summary>
    /// 事件类型依赖边。
    /// </summary>
    EventType,

    /// <summary>
    /// 对象创建依赖边。
    /// </summary>
    ObjectCreation,

    /// <summary>
    /// 转换依赖边。
    /// </summary>
    Conversion
}

/// <summary>
/// 表示方法可达性说明。
/// </summary>
public sealed record MethodReachabilityExplanation(IReadOnlyList<MemberId> Path);

/// <summary>
/// 表示数据流摘要。
/// </summary>
public sealed record DataFlowSummary(IReadOnlyList<string> Reads, IReadOnlyList<string> Writes);

/// <summary>
/// 表示单个 switch case 摘要。
/// </summary>
public sealed record SwitchCaseSummary(string Label, bool FallsThrough);

/// <summary>
/// 表示 switch 流程摘要。
/// </summary>
public sealed record SwitchFlowSummary(IReadOnlyList<SwitchCaseSummary> Cases);

/// <summary>
/// 表示调用链条目。
/// </summary>
public sealed record CallChainEntry(string MemberId, string DisplayName);

/// <summary>
/// 表示调用链分析摘要。
/// </summary>
public sealed record CallChainAnalysisSummary(IReadOnlyList<CallChainEntry> Entries);

/// <summary>
/// 表示高级分析摘要。
/// </summary>
public sealed record AdvancedAnalysisSummary(int PersistentTypeCount = 0, int RiskyTypeCount = 0, IReadOnlyList<string>? Notes = null);

/// <summary>
/// 表示成员清理分析信息。
/// </summary>
public sealed record MemberCleanupSymbolInfo(
    string SymbolId,
    MemberKind MemberKind,
    string DeclaringTypeId,
    string DocumentPath,
    string Name,
    bool IsPublic,
    bool IsPrivate,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsExtern,
    bool IsOrdinaryMethod,
    bool IsPartialType,
    bool IsNestedType,
    bool IsInInterfaceType,
    bool IsEntryPointLike);

/// <summary>
/// 表示类型清理分析信息。
/// </summary>
public sealed record MemberCleanupTypeInfo(
    string TypeId,
    string DocumentPath,
    string Name,
    bool IsPublic,
    bool IsAbstract,
    bool IsStatic,
    bool IsPartial,
    bool IsNested,
    bool IsInterface,
    bool IsInInheritanceChain);

/// <summary>
/// 表示语句图快照。
/// </summary>
public sealed record StatementGraphSnapshot(
    string SeedTargetKey,
    StatementScopeMode ScopeMode,
    MemberId BoundaryMemberId,
    IReadOnlyList<string> Nodes,
    IReadOnlyList<StatementDependencyEdge> Edges)
{
    /// <summary>
    /// 使用简化节点和边集合构造语句图快照。
    /// </summary>
    public StatementGraphSnapshot(IReadOnlyList<string> Nodes, IReadOnlyList<StatementDependencyEdge> Edges)
        : this(string.Empty, StatementScopeMode.MinimalBlock, new MemberId(string.Empty), Nodes, Edges)
    {
    }
}
