using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Core;

/// <summary>
/// 运行模式枚举。
/// </summary>
public enum RunMode
{
    /// <summary>
    /// 标准模式，执行完整流程。
    /// </summary>
    Standard,
    /// <summary>
    /// 仅分析模式。
    /// </summary>
    AnalyzeOnly,
    /// <summary>
    /// 仅规划模式。
    /// </summary>
    PlanOnly
}

/// <summary>
/// 工作区加载偏好枚举。
/// </summary>
public enum WorkspaceLoaderPreference
{
    /// <summary>
    /// 自动选择。
    /// </summary>
    Auto,
    /// <summary>
    /// 优先使用 CodeAnalysis。
    /// </summary>
    CodeAnalysisFirst,
    /// <summary>
    /// 仅使用源码扫描。
    /// </summary>
    SourceOnly
}

/// <summary>
/// 工作区加载模式枚举。
/// </summary>
public enum WorkspaceLoadMode
{
    CodeAnalysis,
    SourceOnly,
    CodeAnalysisFallbackToSourceOnly
}

/// <summary>
/// 工作区加载诊断级别。
/// </summary>
public enum WorkspaceLoadDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 失败代码枚举。
/// </summary>
public enum FailureCode
{
    /// <summary>
    /// 无失败。
    /// </summary>
    None,
    /// <summary>
    /// 工作区加载失败。
    /// </summary>
    WorkspaceLoadFailed,
    /// <summary>
    /// 分析失败。
    /// </summary>
    AnalysisFailed,
    /// <summary>
    /// 计划编译失败。
    /// </summary>
    PlanCompileFailed,
    /// <summary>
    /// 重写失败。
    /// </summary>
    RewriteFailed,
    /// <summary>
    /// 报告生成失败。
    /// </summary>
    ReportFailed
}

/// <summary>
/// 成员类型枚举。
/// </summary>
public enum MemberKind
{
    /// <summary>
    /// 未知类型。
    /// </summary>
    Unknown,
    /// <summary>
    /// 类。
    /// </summary>
    Class,
    /// <summary>
    /// 字段。
    /// </summary>
    Field,
    /// <summary>
    /// 方法。
    /// </summary>
    Method,
    /// <summary>
    /// 构造函数。
    /// </summary>
    Constructor,
    /// <summary>
    /// 属性。
    /// </summary>
    Property,
    /// <summary>
    /// 访问器。
    /// </summary>
    Accessor
}

/// <summary>
/// 目标类型枚举。
/// </summary>
public enum TargetKind
{
    /// <summary>
    /// 语句。
    /// </summary>
    Statement,
    /// <summary>
    /// 方法。
    /// </summary>
    Method,
    /// <summary>
    /// 类。
    /// </summary>
    Class
}

/// <summary>
/// 语句类型引用枚举。
/// 用于区分控制流语句（If, While, For, Return）和其他语句，
/// 以便在规则引擎中应用特定的分析逻辑（如 DirectiveSeedRule 和 ExpressionProjectionRule）。
/// </summary>
public enum StatementKindRef
{
    /// <summary>
    /// 未知类型。
    /// </summary>
    Unknown,
    /// <summary>
    /// 初始化器。
    /// </summary>
    Initializer,
    /// <summary>
    /// 声明。
    /// </summary>
    Declaration,
    /// <summary>
    /// 赋值。
    /// </summary>
    Assignment,
    /// <summary>
    /// If 语句。
    /// </summary>
    If,
    /// <summary>
    /// While 循环。
    /// </summary>
    While,
    /// <summary>
    /// For 循环。
    /// </summary>
    For,
    /// <summary>
    /// 返回语句。
    /// </summary>
    Return,
    /// <summary>
    /// 对象初始化器赋值。
    /// </summary>
    ObjectInitializerAssignment
}

/// <summary>
/// 语句分析作用域模式。
/// </summary>
public enum StatementScopeMode
{
    /// <summary>
    /// 最小块范围。
    /// </summary>
    MinimalBlock,
    /// <summary>
    /// 穿透父级块范围。
    /// </summary>
    ParentBlockPiercing
}

/// <summary>
/// StatementGraph 物化状态。
/// </summary>
public enum StatementGraphMaterialization
{
    None,
    SnapshotOnly,
    Full
}

/// <summary>
/// FunctionGraph 物化状态。
/// </summary>
public enum FunctionGraphMaterialization
{
    None,
    WholeProject,
    ExpandedMembers
}

/// <summary>
/// 边界提升类型。
/// </summary>
public enum BoundaryKind
{
    Invocation
}

/// <summary>
/// 语句分析作用域选项。
/// </summary>
public sealed record ScopeAnalysisOptions(
    StatementScopeMode StatementScopeMode)
{
    public static ScopeAnalysisOptions Default { get; } =
        new(StatementScopeMode.MinimalBlock);
}

/// <summary>
/// 计划操作类型枚举。
/// 添加返回语句时同时添加默认值
/// 要忽略属性这种东西
/// </summary>
public enum PlanActionKind
{
    /// <summary>
    /// 删除。
    /// </summary>
    Delete,
    /// <summary>
    /// 注释掉。
    /// </summary>
    CommentOut,
    /// <summary>
    /// 替换为默认值。
    /// </summary>
    ReplaceWithDefault,
    /// <summary>
    /// 添加返回语句。
    /// </summary>
    AddReturn
}

/// <summary>
/// 成员 ID 结构。
/// </summary>
/// <param name="Value">ID 值。</param>
public readonly record struct MemberId(string Value)
{
    /// <summary>
    /// 返回 ID 的字符串表示。
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// 工作区加载选项。
/// </summary>
/// <param name="PreferredLoader">首选加载器。</param>
/// <param name="AllowFallbackToSourceOnly">是否允许回退到源码扫描。</param>
public sealed record WorkspaceLoadOptions(
    WorkspaceLoaderPreference PreferredLoader,
    bool AllowFallbackToSourceOnly)
{
    public static WorkspaceLoadOptions Default { get; } =
        new(WorkspaceLoaderPreference.Auto, true);
}

/// <summary>
/// 工作区加载诊断。
/// </summary>
/// <param name="Stage">诊断阶段。</param>
/// <param name="Severity">诊断级别。</param>
/// <param name="Message">诊断消息。</param>
public sealed record WorkspaceLoadDiagnostic(
    string Stage,
    WorkspaceLoadDiagnosticSeverity Severity,
    string Message);

/// <summary>
/// 源码文档记录。
/// </summary>
/// <param name="SourcePath">源码绝对路径。</param>
/// <param name="RelativePath">相对路径。</param>
/// <param name="SourceText">源码内容。</param>
public sealed record SourceDocument(
    string SourcePath,
    string RelativePath,
    string SourceText);

/// <summary>
/// 分析输入抽象。
/// </summary>
public abstract record AnalysisInput(string RootPath);

/// <summary>
/// 纯源码分析输入。
/// </summary>
public sealed record SourceOnlyAnalysisInput(
    string RootPath,
    IReadOnlyList<SourceDocument> Documents) : AnalysisInput(RootPath);

/// <summary>
/// Workspace 文档上下文。
/// </summary>
public sealed record WorkspaceDocumentContext(
    Document Document,
    SourceDocument SourceDocument,
    Compilation Compilation,
    SemanticModel SemanticModel,
    SyntaxNode Root);

/// <summary>
/// Workspace 分析输入。
/// </summary>
public sealed record WorkspaceAnalysisInput(
    Solution Solution,
    Project? Project,
    string RootPath,
    IReadOnlyList<WorkspaceDocumentContext> Documents) : AnalysisInput(RootPath);

/// <summary>
/// 运行请求记录。
/// </summary>
/// <param name="InputPath">输入路径。</param>
/// <param name="OutputPath">输出路径。</param>
/// <param name="RuleSet">规则集。</param>
/// <param name="Mode">运行模式。</param>
public sealed record RunRequest(
    string InputPath,
    string OutputPath,
    IReadOnlyList<string> RuleSet,
    RunMode Mode,
    WorkspaceLoadOptions WorkspaceLoadOptions)
{
    public RunRequest(string inputPath, string outputPath, IReadOnlyList<string> ruleSet, RunMode mode)
        : this(inputPath, outputPath, ruleSet, mode, WorkspaceLoadOptions.Default)
    {
    }
}

/// <summary>
/// 运行结果记录。
/// </summary>
/// <param name="IsSuccess">是否成功。</param>
/// <param name="FailureCode">失败代码。</param>
/// <param name="OutputPath">输出路径。</param>
/// <param name="ReportPath">报告路径。</param>
/// <param name="Message">消息。</param>
public sealed record RunResult(
    bool IsSuccess,
    FailureCode FailureCode,
    string OutputPath,
    string? ReportPath,
    string? Message)
{
    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static RunResult Success(string outputPath, string? reportPath) =>
        new(true, FailureCode.None, outputPath, reportPath, null);

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static RunResult Failure(FailureCode code, string outputPath, string? message) =>
        new(false, code, outputPath, null, message);
}

/// <summary>
/// 计划元数据记录。
/// </summary>
/// <param name="ToolName">工具名称。</param>
/// <param name="PlanVersion">计划版本。</param>
/// <param name="InputPath">输入路径。</param>
/// <param name="OutputPath">输出路径。</param>
/// <param name="RunMode">运行模式。</param>
public sealed record PlanMetadata(
    string ToolName,
    string PlanVersion,
    string InputPath,
    string OutputPath,
    RunMode RunMode)
{
    /// <summary>
    /// 生成时间（UTC）。
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 计划目标记录。
/// </summary>
/// <param name="DocumentPath">文档路径。</param>
/// <param name="MemberId">成员 ID。</param>
/// <param name="MemberKind">成员类型。</param>
/// <param name="TargetKind">目标类型。</param>
/// <param name="SpanStart">跨度起始位置。</param>
/// <param name="SpanLength">跨度长度。</param>
/// <param name="DisplayText">显示文本。</param>
public sealed record PlanTarget(
    string DocumentPath,
    MemberId MemberId,
    MemberKind MemberKind,
    TargetKind TargetKind,
    int SpanStart,
    int SpanLength,
    string DisplayText)
{
    /// <summary>
    /// 目标唯一键。
    /// </summary>
    public string TargetKey => $"{DocumentPath}|{MemberId.Value}|{TargetKind}|{SpanStart}|{SpanLength}";
}

/// <summary>
/// 计划操作记录。
/// </summary>
/// <param name="Kind">操作类型。</param>
/// <param name="Payload">负载数据。</param>
public sealed record PlanAction(
    PlanActionKind Kind,
    string? Payload = null);

/// <summary>
/// 计划原因记录。
/// </summary>
/// <param name="RuleId">规则 ID。</param>
/// <param name="ReasonText">原因文本。</param>
/// <param name="SourceTargetKey">源目标键。</param>
/// <param name="SourceTargetDisplayText">源目标显示文本。</param>
/// <param name="RelatedSymbolKeys">相关符号键。</param>
/// <param name="RelatedSymbolNames">相关符号名称。</param>
/// <param name="Severity">严重程度。</param>
public sealed record PlanReason(
    string RuleId,
    string ReasonText,
    string? SourceTargetKey = null,
    string? SourceTargetDisplayText = null,
    IReadOnlyList<string>? RelatedSymbolKeys = null,
    IReadOnlyList<string>? RelatedSymbolNames = null,
    string? Severity = null,
    string? SourceMemberId = null,
    BoundaryKind? BoundaryKind = null,
    IReadOnlyList<string>? TriggeredSymbolKeys = null);

/// <summary>
/// 传播证据记录。
/// </summary>
/// <param name="RelatedSymbolKeys">相关符号键列表。</param>
/// <param name="RelatedSymbolNames">相关符号名称列表。</param>
public sealed record PropagationEvidence(
    IReadOnlyList<string> RelatedSymbolKeys,
    IReadOnlyList<string> RelatedSymbolNames);

/// <summary>
/// 传播跳跃记录。
/// </summary>
/// <param name="FromTargetKey">起始目标键。</param>
/// <param name="FromTargetDisplayText">起始目标显示文本。</param>
/// <param name="ToTargetKey">目标目标键。</param>
/// <param name="ToTargetDisplayText">目标目标显示文本。</param>
/// <param name="RuleId">规则 ID。</param>
/// <param name="ActionKind">操作类型。</param>
/// <param name="Evidence">证据。</param>
public sealed record PropagationHop(
    string FromTargetKey,
    string FromTargetDisplayText,
    string ToTargetKey,
    string ToTargetDisplayText,
    string RuleId,
    PlanActionKind ActionKind,
    PropagationEvidence Evidence);

/// <summary>
/// 传播链记录。
/// </summary>
/// <param name="RootTargetKey">根目标键。</param>
/// <param name="RootTargetDisplayText">根目标显示文本。</param>
/// <param name="Hops">跳跃列表。</param>
public sealed record PropagationChain(
    string RootTargetKey,
    string RootTargetDisplayText,
    IReadOnlyList<PropagationHop> Hops);

/// <summary>
/// 标记决策记录。
/// </summary>
/// <param name="Target">计划目标。</param>
/// <param name="Action">计划操作。</param>
/// <param name="Reason">计划原因。</param>
/// <param name="Chain">传播链。</param>
public sealed record MarkDecision(
    PlanTarget Target,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null)
{
    /// <summary>
    /// 为目标创建标记决策。
    /// </summary>
    public static MarkDecision ForTarget(
        PlanTarget target,
        PlanActionKind actionKind,
        string ruleId,
        string reasonText,
        string? payload = null,
        string? sourceTargetKey = null,
        string? sourceTargetDisplayText = null,
        IReadOnlyList<string>? relatedSymbolKeys = null,
        IReadOnlyList<string>? relatedSymbolNames = null,
        string? severity = null,
        string? sourceMemberId = null,
        BoundaryKind? boundaryKind = null,
        IReadOnlyList<string>? triggeredSymbolKeys = null,
        PropagationChain? chain = null) =>
        new(
            target,
            new PlanAction(actionKind, payload),
            new PlanReason(
                ruleId,
                reasonText,
                sourceTargetKey,
                sourceTargetDisplayText,
                relatedSymbolKeys ?? Array.Empty<string>(),
                relatedSymbolNames ?? Array.Empty<string>(),
                severity,
                sourceMemberId,
                boundaryKind,
                triggeredSymbolKeys ?? Array.Empty<string>()),
            chain);
}

/// <summary>
/// 计划变更记录。
/// </summary>
/// <param name="ExecutionOrder">执行顺序。</param>
/// <param name="Target">计划目标。</param>
/// <param name="Action">计划操作。</param>
/// <param name="Reason">计划原因。</param>
/// <param name="Chain">传播链。</param>
public sealed record PlannedChange(
    int ExecutionOrder,
    PlanTarget Target,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null);

/// <summary>
/// 计划冲突记录。
/// </summary>
/// <param name="ConflictCode">冲突代码。</param>
/// <param name="Target">计划目标。</param>
/// <param name="ActionKinds">操作类型列表。</param>
/// <param name="Reason">原因。</param>
public sealed record PlanConflict(
    string ConflictCode,
    PlanTarget Target,
    IReadOnlyList<PlanActionKind> ActionKinds,
    string Reason);

/// <summary>
/// 审计计划记录。
/// </summary>
/// <param name="Metadata">计划元数据。</param>
/// <param name="Changes">计划变更列表。</param>
/// <param name="Conflicts">计划冲突列表。</param>
public sealed record AuditPlan(
    PlanMetadata Metadata,
    IReadOnlyList<PlannedChange> Changes,
    IReadOnlyList<PlanConflict> Conflicts);

/// <summary>
/// 类型依赖类型枚举。
/// 描述类型之间的静态依赖关系，如继承、实现、字段类型等。
/// </summary>
public enum TypeDependencyKind
{
    /// <summary>
    /// 继承。
    /// </summary>
    Inherits,
    /// <summary>
    /// 实现。
    /// </summary>
    Implements,
    /// <summary>
    /// 字段类型。
    /// </summary>
    FieldType,
    /// <summary>
    /// 属性类型。
    /// </summary>
    PropertyType,
    /// <summary>
    /// 参数类型。
    /// </summary>
    ParameterType,
    /// <summary>
    /// 返回类型。
    /// </summary>
    ReturnType,
    /// <summary>
    /// 对象创建。
    /// </summary>
    ObjectCreation,
    /// <summary>
    /// 静态成员访问。
    /// </summary>
    StaticMemberAccess,
    /// <summary>
    /// 成员体引用。
    /// </summary>
    MemberBodyReference
}

/// <summary>
/// 函数依赖类型枚举。
/// 描述函数之间的动态依赖关系。
/// 注意：目前 FunctionGraphProvider 主要支持 Calls 类型。
/// 其他类型（Creates, ReadsMember, WritesMember 等）在分析阶段有生成逻辑，
/// 但可能未被完全持久化或在视图中利用。
/// </summary>
public enum FunctionDependencyKind
{
    /// <summary>
    /// 调用。
    /// </summary>
    Calls,
    /// <summary>
    /// 创建。
    /// </summary>
    Creates,
    /// <summary>
    /// 读取成员。
    /// </summary>
    ReadsMember,
    /// <summary>
    /// 写入成员。
    /// </summary>
    WritesMember,
    /// <summary>
    /// 使用属性访问器。
    /// </summary>
    UsesPropertyAccessor
}

/// <summary>
/// 语句依赖类型枚举。
/// </summary>
public enum StatementDependencyKind
{
    /// <summary>
    /// 定义。
    /// </summary>
    Defines,
    /// <summary>
    /// 使用。
    /// </summary>
    Uses,
    /// <summary>
    /// 先于。
    /// </summary>
    Precedes
}

/// <summary>
/// 类型节点引用记录。
/// </summary>
/// <param name="TypeId">类型 ID。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="DocumentPath">文档路径。</param>
public sealed record TypeNodeRef(
    string TypeId,
    string DisplayName,
    string DocumentPath);

/// <summary>
/// 类型依赖边记录。
/// </summary>
/// <param name="SourceTypeId">源类型 ID。</param>
/// <param name="TargetTypeId">目标类型 ID。</param>
/// <param name="Kind">依赖类型。</param>
/// <param name="MemberId">成员 ID。</param>
/// <param name="SymbolKey">符号键。</param>
public sealed record TypeDependencyEdge(
    string SourceTypeId,
    string TargetTypeId,
    TypeDependencyKind Kind,
    string? MemberId = null,
    string? SymbolKey = null);

/// <summary>
/// 类型依赖图记录。
/// </summary>
/// <param name="Nodes">节点列表。</param>
/// <param name="Edges">边列表。</param>
public sealed record TypeDependencyGraph(
    IReadOnlyList<TypeNodeRef> Nodes,
    IReadOnlyList<TypeDependencyEdge> Edges);

/// <summary>
/// 函数节点引用记录。
/// </summary>
/// <param name="MemberId">成员 ID。</param>
/// <param name="MemberKind">成员类型。</param>
/// <param name="DeclaringTypeId">声明类型 ID。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="DocumentPath">文档路径。</param>
/// <param name="SpanStart">跨度起始位置。</param>
/// <param name="SpanLength">跨度长度。</param>
/// <param name="IsPrivate">是否私有。</param>
/// <param name="ReturnsVoid">是否返回 Void。</param>
/// <param name="HasBody">是否有方法体。</param>
/// <param name="HasStatements">是否有语句。</param>
/// <param name="ReturnTypeDisplay">返回类型显示文本。</param>
public sealed record FunctionNodeRef(
    MemberId MemberId,
    MemberKind MemberKind,
    string DeclaringTypeId,
    string DisplayName,
    string DocumentPath,
    int SpanStart,
    int SpanLength,
    bool IsPrivate,
    bool ReturnsVoid,
    bool HasBody,
    bool HasStatements,
    string ReturnTypeDisplay);

/// <summary>
/// 函数依赖边记录。
/// </summary>
/// <param name="SourceMemberId">源成员 ID。</param>
/// <param name="TargetMemberId">目标成员 ID。</param>
/// <param name="Kind">依赖类型。</param>
/// <param name="SymbolKey">符号键。</param>
public sealed record FunctionDependencyEdge(
    MemberId SourceMemberId,
    MemberId TargetMemberId,
    FunctionDependencyKind Kind,
    string? SymbolKey = null);

/// <summary>
/// 函数依赖图记录。
/// </summary>
/// <param name="Nodes">节点列表。</param>
/// <param name="Edges">边列表。</param>
public sealed record FunctionDependencyGraph(
    IReadOnlyList<FunctionNodeRef> Nodes,
    IReadOnlyList<FunctionDependencyEdge> Edges);

/// <summary>
/// 函数删除影响范围集合。
/// </summary>
public sealed record FunctionImpactSet(
    IReadOnlyList<string> DeletedFunctionIds,
    IReadOnlyList<string> AffectedFunctionIds,
    IReadOnlyList<string> AffectedDocumentPaths,
    int ExpansionDepth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds);

/// <summary>
/// 语句依赖边记录。
/// </summary>
/// <param name="SourceTargetKey">源目标键。</param>
/// <param name="TargetTargetKey">目标目标键。</param>
/// <param name="Kind">依赖类型。</param>
/// <param name="SymbolKey">符号键。</param>
public sealed record StatementDependencyEdge(
    string SourceTargetKey,
    string TargetTargetKey,
    StatementDependencyKind Kind,
    string? SymbolKey = null);

/// <summary>
/// 语句依赖图记录。
/// </summary>
/// <param name="Nodes">节点列表。</param>
/// <param name="Edges">边列表。</param>
public sealed record StatementDependencyGraph(
    IReadOnlyList<string> Nodes,
    IReadOnlyList<StatementDependencyEdge> Edges);

/// <summary>
/// 函数索引。
/// </summary>
public sealed record FunctionIndex(
    IReadOnlyDictionary<string, FunctionNodeRef> NodesByMemberId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MemberIdsByDocumentPath)
{
    public static FunctionIndex Empty { get; } = new(
        new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
}

/// <summary>
/// 函数事实记录。
/// </summary>
public sealed record FunctionFact(
    FunctionNodeRef Node,
    IReadOnlyList<MemberId> CalledMemberIds);

/// <summary>
/// 函数事实索引。
/// </summary>
public sealed record FunctionFactsIndex(
    IReadOnlyDictionary<string, FunctionFact> FactsByMemberId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MemberIdsByDocumentPath,
    IReadOnlyDictionary<string, IReadOnlyList<MemberId>> IncomingCallersByMemberId)
{
    public static FunctionFactsIndex Empty { get; } = new(
        new Dictionary<string, FunctionFact>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<MemberId>>(StringComparer.Ordinal));
}

/// <summary>
/// 函数图范围。
/// </summary>
public enum FunctionGraphScope
{
    WholeProject,
    ExpandedMembers
}

/// <summary>
/// 函数图快照。
/// </summary>
public sealed record FunctionGraphSnapshot(
    FunctionGraphScope Scope,
    IReadOnlyList<MemberId> RootMemberIds,
    IReadOnlyList<string> IncludedDocumentPaths,
    FunctionDependencyGraph Graph);

/// <summary>
/// 函数图请求。
/// </summary>
public sealed record FunctionGraphRequest(
    FunctionGraphScope Scope,
    IReadOnlyList<MemberId> RootMemberIds,
    int Depth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds,
    string Requester,
    string Reason);

/// <summary>
/// Supported function graph request factories for the current analysis stage.
/// </summary>
public static class FunctionGraphRequests
{
    public static FunctionGraphRequest WholeProjectCalls(string requester, string reason) =>
        new(
            FunctionGraphScope.WholeProject,
            Array.Empty<MemberId>(),
            0,
            new[] { FunctionDependencyKind.Calls },
            requester,
            reason);

    public static FunctionGraphRequest ExpandedMembersCalls(
        IReadOnlyList<MemberId> rootMemberIds,
        string requester,
        string reason) =>
        new(
            FunctionGraphScope.ExpandedMembers,
            rootMemberIds,
            1,
            new[] { FunctionDependencyKind.Calls },
            requester,
            reason);
}

/// <summary>
/// 函数图提供器。
/// </summary>
public interface IFunctionGraphProvider
{
    FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request);

    FunctionGraphSnapshot GetWholeProjectSnapshot() =>
        GetSnapshot(FunctionGraphRequests.WholeProjectCalls("IFunctionGraphProvider", "Whole-project function graph snapshot"));

    FunctionGraphSnapshot GetExpandedMembersSnapshot(IReadOnlyList<MemberId> rootMemberIds, int depth = 1)
    {
        if (depth != 1)
        {
            throw new NotSupportedException("ExpandedMembers snapshots currently only support depth = 1.");
        }

        return GetSnapshot(FunctionGraphRequests.ExpandedMembersCalls(
            rootMemberIds,
            "IFunctionGraphProvider",
            "Expanded-members function graph snapshot"));
    }
}

/// <summary>
/// 语句事实记录。
/// </summary>
public sealed record StatementFact(
    string TargetKey,
    MemberId MemberId,
    StatementKindRef StatementKind,
    IReadOnlyList<SymbolRef> DefinesSymbols,
    IReadOnlyList<SymbolRef> UsesSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds,
    StatementScopeMode ScopeMode,
    string? ScopeId,
    string? ParentScopeId,
    int SpanStart,
    int SpanLength);

/// <summary>
/// 语句事实索引。
/// </summary>
public sealed record StatementFactsIndex(
    IReadOnlyDictionary<string, IReadOnlyList<StatementFact>> FactsByMemberId)
{
    public static StatementFactsIndex Empty { get; } =
        new(new Dictionary<string, IReadOnlyList<StatementFact>>(StringComparer.Ordinal));
}

/// <summary>
/// 局部语句依赖图快照。
/// </summary>
public sealed record StatementGraphSnapshot(
    string SeedTargetKey,
    StatementScopeMode ScopeMode,
    MemberId BoundaryMemberId,
    IReadOnlyList<string> Nodes,
    IReadOnlyList<StatementDependencyEdge> Edges);

/// <summary>
/// 语句级分析服务。
/// </summary>
public interface IStatementAnalysisService
{
    StatementGraphSnapshot Analyze(PlanTarget seedTarget, StatementScopeMode scopeMode);
}

/// <summary>
/// 分析视图记录。
/// </summary>
/// <param name="Targets">分析目标列表。</param>
/// <param name="Edges">分析边列表。</param>
/// <param name="TypeGraph">类型依赖图。</param>
/// <param name="FunctionGraph">函数依赖图。</param>
/// <param name="StatementGraph">语句依赖图。</param>
public sealed record AnalysisView(
    IReadOnlyList<AnalysisTarget> Targets,
    IReadOnlyList<AnalysisEdge> Edges,
    TypeDependencyGraph TypeGraph,
    FunctionDependencyGraph FunctionGraph,
    StatementDependencyGraph StatementGraph,
    StatementGraphMaterialization StatementGraphMaterialization,
    FunctionGraphMaterialization FunctionGraphMaterialization);

/// <summary>
/// 全局分析事实目录。
/// </summary>
public sealed record AnalysisSnapshot(
    AnalysisView View,
    FunctionIndex FunctionIndex,
    FunctionFactsIndex FunctionFacts,
    StatementFactsIndex StatementFacts);

/// <summary>
/// 分析查询服务集合。
/// </summary>
public sealed record AnalysisServices(
    IInheritanceQueryService Inheritance,
    IReferenceQueryService References,
    IStatementAnalysisService Statements,
    IFunctionGraphProvider FunctionGraphs);

/// <summary>
/// 单次规则或预测执行上下文。
/// </summary>
public sealed record RuleExecutionContext(
    string Requester,
    PlanTarget? SeedTarget,
    StatementScopeMode StatementScopeMode,
    CancellationToken CancellationToken,
    string? Reason = null);

/// <summary>
/// 分析目标记录。
/// </summary>
/// <param name="Target">计划目标。</param>
/// <param name="IsHighRisk">是否高风险。</param>
/// <param name="Directives">指令列表。</param>
/// <param name="DefinesSymbols">定义符号列表。</param>
/// <param name="UsesSymbols">使用符号列表。</param>
/// <param name="StatementKind">语句类型。</param>
/// <param name="IsSanitizingAssignment">是否为净化赋值。</param>
/// <param name="IsObjectInitializerAssignment">是否为对象初始化器赋值。</param>
/// <param name="HasMarkedExpressionSeed">是否有标记的表达式种子。</param>
/// <param name="MarkedExpressionKinds">标记的表达式类型。</param>
public sealed record AnalysisTarget(
    PlanTarget Target,
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
/// 分析边类型枚举。
/// </summary>
public enum AnalysisEdgeKind
{
    /// <summary>
    /// 定义。
    /// </summary>
    Defines,
    /// <summary>
    /// 使用。
    /// </summary>
    Uses,
    /// <summary>
    /// 先于。
    /// </summary>
    Precedes
}

/// <summary>
/// 符号类型引用枚举。
/// </summary>
public enum SymbolKindRef
{
    /// <summary>
    /// 未知。
    /// </summary>
    Unknown,
    /// <summary>
    /// 局部变量。
    /// </summary>
    Local,
    /// <summary>
    /// 参数。
    /// </summary>
    Parameter,
    /// <summary>
    /// 字段。
    /// </summary>
    Field,
    /// <summary>
    /// 属性。
    /// </summary>
    Property
}

/// <summary>
/// 符号引用记录。
/// </summary>
/// <param name="SymbolKey">符号键。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="SymbolKind">符号类型。</param>
/// <param name="DeclaringMemberId">声明成员 ID。</param>
/// <param name="DeclarationSpanStart">声明跨度起始位置。</param>
/// <param name="DeclarationSpanLength">声明跨度长度。</param>
public sealed record SymbolRef(
    string SymbolKey,
    string DisplayName,
    SymbolKindRef SymbolKind,
    MemberId DeclaringMemberId,
    int DeclarationSpanStart,
    int DeclarationSpanLength);

/// <summary>
/// 分析边记录。
/// </summary>
/// <param name="SourceTargetKey">源目标键。</param>
/// <param name="TargetTargetKey">目标目标键。</param>
/// <param name="Kind">边类型。</param>
/// <param name="SymbolKey">符号键。</param>
public sealed record AnalysisEdge(
    string SourceTargetKey,
    string TargetTargetKey,
    AnalysisEdgeKind Kind,
    string? SymbolKey = null);

/// <summary>
/// 继承查询服务接口。
/// </summary>
public interface IInheritanceQueryService
{
    /// <summary>
    /// 检查成员是否为重写成员。
    /// </summary>
    bool IsOverrideMember(string memberId);
    /// <summary>
    /// 检查成员是否实现接口成员。
    /// </summary>
    bool ImplementsInterfaceMember(string memberId);
    /// <summary>
    /// 检查类型是否在继承链中。
    /// </summary>
    bool IsInInheritanceChain(string typeId);
}

/// <summary>
/// 引用查询服务接口。
/// </summary>
public interface IReferenceQueryService
{
    /// <summary>
    /// 检查符号或成员是否有引用。
    /// </summary>
    bool HasReferences(string symbolOrMemberId);
    /// <summary>
    /// 获取引用该符号的函数列表。
    /// </summary>
    IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId);
    /// <summary>
    /// 获取引用该符号的类型列表。
    /// </summary>
    IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId);
}

/// <summary>
/// 指令操作记录。
/// </summary>
/// <param name="ActionKind">操作类型。</param>
/// <param name="Payload">负载数据。</param>
/// <param name="RuleId">规则 ID。</param>
/// <param name="ReasonText">原因文本。</param>
public sealed record DirectiveAction(
    PlanActionKind ActionKind,
    string? Payload,
    string RuleId,
    string ReasonText);

/// <summary>
/// 工作区加载结果。
/// </summary>
/// <param name="IsSuccess">是否成功。</param>
/// <param name="Documents">加载后的源码文档。</param>
/// <param name="LoadMode">实际加载模式。</param>
/// <param name="PrimaryLoader">主加载器名称。</param>
/// <param name="FallbackUsed">是否发生了回退。</param>
/// <param name="Diagnostics">加载诊断。</param>
public sealed record WorkspaceLoadResult(
    bool IsSuccess,
    AnalysisInput? AnalysisInput,
    IReadOnlyList<SourceDocument> Documents,
    WorkspaceLoadMode LoadMode,
    string PrimaryLoader,
    bool FallbackUsed,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics)
{
    public static WorkspaceLoadResult Success(
        AnalysisInput analysisInput,
        WorkspaceLoadMode loadMode,
        string primaryLoader,
        bool fallbackUsed = false,
        IReadOnlyList<WorkspaceLoadDiagnostic>? diagnostics = null) =>
        new(
            true,
            analysisInput,
            ExtractDocuments(analysisInput),
            loadMode,
            primaryLoader,
            fallbackUsed,
            diagnostics ?? Array.Empty<WorkspaceLoadDiagnostic>());

    public static WorkspaceLoadResult Success(
        IReadOnlyList<SourceDocument> documents,
        WorkspaceLoadMode loadMode,
        string primaryLoader,
        bool fallbackUsed = false,
        IReadOnlyList<WorkspaceLoadDiagnostic>? diagnostics = null) =>
        new(
            true,
            new SourceOnlyAnalysisInput(ResolveRootPath(documents), documents),
            documents,
            loadMode,
            primaryLoader,
            fallbackUsed,
            diagnostics ?? Array.Empty<WorkspaceLoadDiagnostic>());

    public static WorkspaceLoadResult Failure(
        WorkspaceLoadMode loadMode,
        string primaryLoader,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics) =>
        new(false, null, Array.Empty<SourceDocument>(), loadMode, primaryLoader, false, diagnostics);

    private static IReadOnlyList<SourceDocument> ExtractDocuments(AnalysisInput analysisInput)
    {
        return analysisInput switch
        {
            SourceOnlyAnalysisInput sourceOnly => sourceOnly.Documents,
            WorkspaceAnalysisInput workspace => workspace.Documents
                .Select(document => document.SourceDocument)
                .ToArray(),
            _ => Array.Empty<SourceDocument>()
        };
    }

    private static string ResolveRootPath(IReadOnlyList<SourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            return string.Empty;
        }

        if (documents.Count == 1)
        {
            return Path.GetDirectoryName(documents[0].SourcePath) ?? string.Empty;
        }

        return Path.GetDirectoryName(documents[0].SourcePath) ?? string.Empty;
    }
}

/// <summary>
/// 计划编译结果记录。
/// </summary>
/// <param name="IsSuccess">是否成功。</param>
/// <param name="Plan">审计计划。</param>
/// <param name="FailureCode">失败代码。</param>
/// <param name="Conflicts">冲突列表。</param>
/// <param name="Message">消息。</param>
public sealed record PlanCompilationResult(
    bool IsSuccess,
    AuditPlan? Plan,
    FailureCode FailureCode,
    IReadOnlyList<PlanConflict> Conflicts,
    string? Message)
{
    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static PlanCompilationResult Success(AuditPlan plan) =>
        new(true, plan, FailureCode.None, Array.Empty<PlanConflict>(), null);

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static PlanCompilationResult Failure(string? message, IReadOnlyList<PlanConflict> conflicts) =>
        new(false, null, FailureCode.PlanCompileFailed, conflicts, message);
}

/// <summary>
/// 重写执行结果记录。
/// </summary>
/// <param name="IsSuccess">是否成功。</param>
/// <param name="FailureCode">失败代码。</param>
/// <param name="RewrittenSource">重写后的源代码。</param>
/// <param name="Message">消息。</param>
public sealed record RewriteExecutionResult(
    bool IsSuccess,
    FailureCode FailureCode,
    string? RewrittenSource,
    string? Message)
{
    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static RewriteExecutionResult Success(string rewrittenSource) =>
        new(true, FailureCode.None, rewrittenSource, null);

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static RewriteExecutionResult Failure(string? message) =>
        new(false, FailureCode.RewriteFailed, null, message);
}

/// <summary>
/// 失败摘要记录。
/// </summary>
/// <param name="FailureCode">失败代码。</param>
/// <param name="Message">消息。</param>
public sealed record FailureSummary(
    FailureCode FailureCode,
    string Message);

/// <summary>
/// 冲突摘要记录。
/// </summary>
/// <param name="ConflictCode">冲突代码。</param>
/// <param name="TargetKey">目标键。</param>
/// <param name="TargetDisplayText">目标显示文本。</param>
/// <param name="ActionKinds">操作类型列表。</param>
/// <param name="Reason">原因。</param>
public sealed record ConflictSummary(
    string ConflictCode,
    string TargetKey,
    string TargetDisplayText,
    IReadOnlyList<PlanActionKind> ActionKinds,
    string Reason);

/// <summary>
/// 风险摘要记录。
/// </summary>
/// <param name="SkippedHighRiskTargetCount">跳过的高风险目标数量。</param>
/// <param name="SampleTargetDisplayTexts">示例目标显示文本列表。</param>
public sealed record RiskSummary(
    int SkippedHighRiskTargetCount,
    IReadOnlyList<string> SampleTargetDisplayTexts);

/// <summary>
/// 计划覆盖率摘要记录。
/// </summary>
/// <param name="CoveredMethodCount">覆盖的方法数量。</param>
/// <param name="CoveredStatementCount">覆盖的语句数量。</param>
/// <param name="SampleCoveredTargetDisplayTexts">示例覆盖目标显示文本列表。</param>
public sealed record PlanCoverageSummary(
    int CoveredMethodCount,
    int CoveredStatementCount,
    IReadOnlyList<string> SampleCoveredTargetDisplayTexts);

/// <summary>
/// 函数删除影响摘要。
/// </summary>
public sealed record FunctionImpactSummary(
    int DeletedFunctionCount,
    int AffectedFunctionCount,
    int AffectedDocumentCount,
    int ExpansionDepth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds,
    IReadOnlyList<string> SampleAffectedFunctionIds,
    IReadOnlyList<string> SampleAffectedDocumentPaths);

/// <summary>
/// 引用归零预测摘要。
/// </summary>
public sealed record ReferenceZeroPredictionSummary(
    int PredictedMethodDeleteCount,
    IReadOnlyList<string> SamplePredictedMethodIds);

/// <summary>
/// 边界提升摘要。
/// </summary>
public sealed record BoundaryPromotionSummary(
    BoundaryKind BoundaryKind,
    int PromotedMethodDeleteCount,
    IReadOnlyList<string> SamplePromotedMethodIds);

/// <summary>
/// 运行报告记录。
/// </summary>
/// <param name="IsSuccess">是否成功。</param>
/// <param name="FailureCode">失败代码。</param>
/// <param name="AnalysisTargets">分析目标数量。</param>
/// <param name="PlannedChanges">计划变更数量。</param>
/// <param name="Conflicts">冲突数量。</param>
/// <param name="RewrittenDocuments">重写文档数量。</param>
/// <param name="GeneratedArtifacts">生成的制品列表。</param>
/// <param name="FailureSummary">失败摘要。</param>
/// <param name="ConflictSummaries">冲突摘要列表。</param>
/// <param name="RiskSummary">风险摘要。</param>
/// <param name="PlanCoverageSummary">计划覆盖率摘要。</param>
/// <param name="Message">消息。</param>
public sealed record RunReport(
    bool IsSuccess,
    FailureCode FailureCode,
    int AnalysisTargets,
    int PlannedChanges,
    int Conflicts,
    int RewrittenDocuments,
    IReadOnlyList<string> GeneratedArtifacts,
    FailureSummary? FailureSummary,
    IReadOnlyList<ConflictSummary> ConflictSummaries,
    RiskSummary RiskSummary,
    PlanCoverageSummary PlanCoverageSummary,
    FunctionImpactSummary? FunctionImpactSummary,
    BoundaryPromotionSummary? BoundaryPromotionSummary,
    ReferenceZeroPredictionSummary? ReferenceZeroPredictionSummary,
    WorkspaceLoadMode WorkspaceLoadMode,
    bool WorkspaceFallbackUsed,
    IReadOnlyList<WorkspaceLoadDiagnostic> WorkspaceDiagnostics,
    string? Message);
