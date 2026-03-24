using TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Core.Analysis;

/// <summary>
/// 表示单个分析输入源码文档。
/// </summary>
public sealed record SourceDocument(
    string SourcePath,
    string RelativePath,
    string SourceText);

/// <summary>
/// 表示分析阶段使用的源码文档集合。
/// </summary>
public sealed record SourceDocumentSet(
    string EntryPath,
    string RootPath,
    IReadOnlyList<SourceDocument> Documents);

/// <summary>
/// 表示分析输入来源模式。
/// </summary>
public enum AnalysisInputMode
{
    /// <summary>
    /// 仅基于源码输入。
    /// </summary>
    SourceOnly,

    /// <summary>
    /// 基于工作区输入。
    /// </summary>
    Workspace
}

/// <summary>
/// 表示分析环境信息。
/// </summary>
public sealed record AnalysisEnvironmentInfo(
    string EnvironmentKind,
    string? ProjectPath = null,
    string? SolutionPath = null,
    string? RequestedPrimaryLoader = null);

/// <summary>
/// 表示分析输入对象。
/// </summary>
public sealed record AnalysisInput(
    SourceDocumentSet SourceSet,
    AnalysisInputMode InputMode,
    AnalysisEnvironmentInfo? Environment = null);

/// <summary>
/// “从源码标记里解析出来的一条指令动作”。它本身不是最终重写结果，而是规则引擎的输入之一
/// </summary>
public sealed record DirectiveAction(
    PlanActionKind ActionKind,
    string? Payload,
    string RuleId,
    string ReasonText);

/// <summary>
/// 表示分析目标图中的边类型。
/// </summary>
public enum AnalysisEdgeKind
{
    /// <summary>
    /// 定义边。
    /// </summary>
    Defines,

    /// <summary>
    /// 使用边。
    /// </summary>
    Uses,

    /// <summary>
    /// 表示“源目标在执行顺序上先于目标目标”。
    /// </summary>
    Precedes
}

/// <summary>
/// 表示符号引用类型。
/// </summary>
public enum SymbolKindRef
{
    /// <summary>
    /// 未知符号类型。
    /// </summary>
    Unknown,

    /// <summary>
    /// 局部变量符号。
    /// </summary>
    Local,

    /// <summary>
    /// 参数符号。
    /// </summary>
    Parameter,

    /// <summary>
    /// 字段符号。
    /// </summary>
    Field,

    /// <summary>
    /// 属性符号。
    /// </summary>
    Property
}

/// <summary>
/// 表示类型依赖边类型。
/// </summary>
public enum TypeDependencyKind
{
    /// <summary>
    /// 继承依赖。
    /// </summary>
    Inherits,

    /// <summary>
    /// 实现依赖。
    /// </summary>
    Implements,

    /// <summary>
    /// 字段类型依赖。
    /// </summary>
    FieldType,

    /// <summary>
    /// 属性类型依赖。
    /// </summary>
    PropertyType,

    /// <summary>
    /// 参数类型依赖。
    /// </summary>
    ParameterType,

    /// <summary>
    /// 返回类型依赖。
    /// </summary>
    ReturnType,

    /// <summary>
    /// 对象创建依赖。
    /// </summary>
    ObjectCreation,

    /// <summary>
    /// 静态成员访问依赖。
    /// </summary>
    StaticMemberAccess,

    /// <summary>
    /// 成员体引用依赖。
    /// </summary>
    MemberBodyReference
}

/// <summary>
/// 表示语句依赖边类型。
/// </summary>
public enum StatementDependencyKind
{
    /// <summary>
    /// 定义边。
    /// </summary>
    Defines,

    /// <summary>
    /// 使用边。
    /// </summary>
    Uses,

    /// <summary>
    /// 顺序前驱边。
    /// </summary>
    Precedes
}

/// <summary>
/// 表示分析目标引用到的符号。
/// </summary>
public sealed record SymbolRef(
    string SymbolKey,
    string DisplayName,
    SymbolKindRef SymbolKind,
    MemberId DeclaringMemberId,
    int DeclarationSpanStart,
    int DeclarationSpanLength);

/// <summary>
/// 表示分析目标之间的边。
/// </summary>
public sealed record AnalysisEdge(
    string SourceTargetKey,
    string TargetTargetKey,
    AnalysisEdgeKind Kind,
    string? SymbolKey = null);

/// <summary>
/// 表示规则层消费的分析目标。
/// </summary>
public sealed record AnalysisTarget(
    TargetIdentity Target,
    TargetLocator Locator,
    bool IsHighRisk,
    IReadOnlyList<DirectiveAction> Directives,
    IReadOnlyList<SymbolRef> DefinesSymbols,
    IReadOnlyList<SymbolRef> UsesSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds,
    StatementKindRef StatementKind,
    bool IsSanitizingAssignment,
    bool IsObjectInitializerAssignment,
    bool HasMarkedExpressionSeed,
    IReadOnlyList<string> MarkedExpressionKinds,
    StatementScopeMode ScopeMode,
    string? ScopeId,
    string? ParentScopeId);

/// <summary>
/// 表示类型图中的节点。
/// </summary>
public sealed record TypeNodeRef(string TypeId, string DisplayName, string DocumentPath);

/// <summary>
/// 表示类型图中的边。
/// </summary>
public sealed record TypeDependencyEdge(string SourceTypeId, string TargetTypeId, TypeDependencyKind Kind, string? MemberId = null, string? SymbolKey = null);

/// <summary>
/// 表示完整类型依赖图。
/// </summary>
public sealed record TypeDependencyGraph(IReadOnlyList<TypeNodeRef> Nodes, IReadOnlyList<TypeDependencyEdge> Edges);

/// <summary>
/// 表示函数图中的节点。
/// </summary>
public sealed record FunctionNodeRef(MemberId MemberId, MemberKind MemberKind, string DeclaringTypeId, string DisplayName, string DocumentPath, int SpanStart, int SpanLength, bool IsPrivate, bool ReturnsVoid, bool HasBody, bool HasStatements, string ReturnTypeDisplay);

/// <summary>
/// 表示函数图中的边。
/// </summary>
public sealed record FunctionDependencyEdge(MemberId SourceMemberId, MemberId TargetMemberId, FunctionDependencyKind Kind, string? SymbolKey = null);

/// <summary>
/// 表示完整函数依赖图。
/// </summary>
public sealed record FunctionDependencyGraph(IReadOnlyList<FunctionNodeRef> Nodes, IReadOnlyList<FunctionDependencyEdge> Edges);

/// <summary>
/// 表示语句图中的边。
/// </summary>
public sealed record StatementDependencyEdge(string SourceTargetKey, string TargetTargetKey, StatementDependencyKind Kind, string? SymbolKey = null);

/// <summary>
/// 表示完整语句依赖图。
/// </summary>
public sealed record StatementDependencyGraph(IReadOnlyList<string> Nodes, IReadOnlyList<StatementDependencyEdge> Edges);

/// <summary>
/// 表示函数事实。
/// </summary>
public sealed record FunctionFact(FunctionNodeRef Node, IReadOnlyList<MemberId> CalledMemberIds);

/// <summary>
/// 表示函数索引。
/// </summary>
public sealed record FunctionIndex(IReadOnlyDictionary<string, FunctionNodeRef> NodesByMemberId, IReadOnlyDictionary<string, IReadOnlyList<string>> MemberIdsByDocumentPath)
{
    /// <summary>
    /// 获取空的函数索引实例。
    /// </summary>
    public static FunctionIndex Empty { get; } = new(new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal), new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
}

/// <summary>
/// 表示函数事实索引。
/// </summary>
public sealed record FunctionFactsIndex(IReadOnlyDictionary<string, FunctionFact> FactsByMemberId, IReadOnlyDictionary<string, IReadOnlyList<string>> MemberIdsByDocumentPath, IReadOnlyDictionary<string, IReadOnlyList<MemberId>> IncomingCallersByMemberId)
{
    /// <summary>
    /// 获取空的函数事实索引实例。
    /// </summary>
    public static FunctionFactsIndex Empty { get; } = new(new Dictionary<string, FunctionFact>(StringComparer.Ordinal), new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), new Dictionary<string, IReadOnlyList<MemberId>>(StringComparer.Ordinal));
}

/// <summary>
/// 表示单个语句事实。
/// </summary>
public sealed record StatementFact(
    string TargetKey,
    MemberId MemberId,
    StatementKindRef StatementKind,
    IReadOnlyList<SymbolRef> DefinedSymbols,
    IReadOnlyList<SymbolRef> UsedSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds,
    StatementScopeMode ScopeMode,
    string? ScopeId,
    string? ParentScopeId,
    int SpanStart,
    int SpanLength)
{
}

/// <summary>
/// 表示语句事实索引。
/// </summary>
public sealed record StatementFactsIndex(
    IReadOnlyDictionary<string, StatementFact> FactsByTargetKey,
    IReadOnlyDictionary<string, IReadOnlyList<StatementFact>> FactsByMemberId)
{
    /// <summary>
    /// 通过目标键索引构造语句事实索引。
    /// </summary>
    public StatementFactsIndex(IReadOnlyDictionary<string, StatementFact> factsByTargetKey)
        : this(factsByTargetKey, BuildFactsByMemberId(factsByTargetKey))
    {
    }

    /// <summary>
    /// 获取空的语句事实索引实例。
    /// </summary>
    public static StatementFactsIndex Empty { get; } = new(
        new Dictionary<string, StatementFact>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<StatementFact>>(StringComparer.Ordinal));

    /// <summary>
    /// 按成员标识构建语句事实索引。
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<StatementFact>> BuildFactsByMemberId(
        IReadOnlyDictionary<string, StatementFact> factsByTargetKey)
    {
        return factsByTargetKey.Values
            .Where(static fact => !string.IsNullOrEmpty(fact.MemberId.Value))
            .GroupBy(static fact => fact.MemberId.Value, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<StatementFact>)group
                    .OrderBy(static fact => fact.SpanStart)
                    .ThenBy(static fact => fact.TargetKey, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }
}

/// <summary>
/// 表示分析阶段对外暴露的统一结果模型。
/// </summary>
public sealed record AnalysisResultModel(IReadOnlyList<AnalysisTarget> Targets, IReadOnlyList<AnalysisEdge> Edges, TypeDependencyGraph TypeGraph, FunctionDependencyGraph FunctionGraph, StatementDependencyGraph StatementGraph, StatementGraphMaterialization StatementGraphMaterialization, FunctionGraphMaterialization FunctionGraphMaterialization);
